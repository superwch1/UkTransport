using Backend.Models;
using Backend.Repositories;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Backend.Services
{
    public class BusScheduleEstimationService : BackgroundService
    {
        private readonly TransportDataStore _transportDataStore;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly TimeService _timeService;
        private readonly ILogger _logger;

        private readonly ScheduleMetaOptions _meta;

        // Minutes to shift a reported departure by when nothing matched it exactly: -1, 1, -2, 2 and so on out to the configured maximum
        private readonly int[] _departureOffsetMinutes;
        private readonly Dictionary<string, LiveBusJourney> _journeyByKey = [];

        public BusScheduleEstimationService(IConfiguration configuration, TransportDataStore transportDataStore, TimeService timeService, IServiceScopeFactory serviceScopeFactory, ILogger<BusScheduleEstimationService> logger)
        {
            _transportDataStore = transportDataStore;
            _timeService = timeService;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;

            _meta = configuration
                .GetSection("Bus")
                .GetSection("Schedule")
                .GetSection("Meta")
                .Get<ScheduleMetaOptions>() ?? throw new InvalidDataException("Bus:Schedule:Meta");

            _departureOffsetMinutes = [.. Enumerable.Range(1, _meta.MaxDepartureOffsetMinutes).SelectMany(x => new[] { -x, x })];
        }


        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    IReadOnlyDictionary<string, BusLocation> busLocationByKey = await _transportDataStore.ReadBusLocationAsync();
                    HashSet<string> notFoundKey = busLocationByKey.Keys.ToHashSet();

                    // early return since the bus stop is not finished imported yet
                    using (IServiceScope stopScope = _serviceScopeFactory.CreateScope())
                    {
                        StopRepository stopRepository = stopScope.ServiceProvider.GetRequiredService<StopRepository>();
                        if (!stopRepository.IsStopFinishedImport())
                        {
                            await Task.Delay(1000);
                            continue;
                        }
                    }
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    // First: search for the journey from memory
                    int cachedMatchCount = 0;
                    foreach (BusLocation busLocation in busLocationByKey.Values)
                    {
                        if (!_journeyByKey.TryGetValue(busLocation.JourneyKey, out LiveBusJourney? journey))
                            continue;

                        cachedMatchCount++;

                        // re-estimate the schedule offset and last seen sequence based on the new bus location
                        (int? lastSeenSequence, int scheduleOffsetMinutes) = EstimateBusSchedule(busLocation, journey.BusCallingPoints, journey.LastArrivedStopSequence, journey.ScheduleOffsetMinutes);
                        _journeyByKey[busLocation.JourneyKey] = journey with
                        {
                            LastArrivedStopSequence = lastSeenSequence,
                            ScheduleOffsetMinutes = scheduleOffsetMinutes,
                            Latitude = busLocation.Latitude,
                            Longitude = busLocation.Longitude,
                            Bearing = busLocation.Bearing,
                            RecordedAtTime = busLocation.RecordedAtTime,
                        };

                        notFoundKey.Remove(busLocation.JourneyKey);
                    }


                    // Second: search for the journey in database using departure-origin-destination key
                    IReadOnlyList<BusLocation> uncachedBusLocations = notFoundKey.Select(x => busLocationByKey[x]).ToList();
                    int exactMatchCount = 0;

                    using (IServiceScope scope = _serviceScopeFactory.CreateScope())
                    {
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();
                        IReadOnlyDictionary<string, BusTimetable> busTimetableByKey = await busRepository.GetBusTimetableByJourneyKey(uncachedBusLocations.Select(x => x.JourneyKey).ToList());

                        foreach ((string journeyKey, BusTimetable timetable) in busTimetableByKey)
                        {
                            if (!busLocationByKey.TryGetValue(journeyKey, out BusLocation? busLocation) || busLocation is null)
                                continue;

                            exactMatchCount++;
                            FirstEstimation(journeyKey, busLocation, timetable);
                            notFoundKey.Remove(journeyKey);
                        }
                    }


                    // Third: shift minutes for each departure-origin-destination key then perform search in database
                    IReadOnlyList<BusLocation> unmatchedBusLocations = notFoundKey.Select(x => busLocationByKey[x]).ToList();
                    int offsetMatchCount = 0;

                    using (IServiceScope scope = _serviceScopeFactory.CreateScope())
                    {
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();
                        IReadOnlyDictionary<string, List<string>> candidateKeysByKey = unmatchedBusLocations.ToDictionary(
                            x => x.JourneyKey,
                            x => _departureOffsetMinutes
                                // it is build from -1, 1, -2, 2, -3, 3 ...
                                .Select(offset => BusTimeTableExtension.BuildJourneyKey(x.LineName, x.OriginAimedDepartureTime.AddMinutes(offset), x.OriginBusStopId, x.DestinationBusStopId))
                                .ToList());

                        IReadOnlyDictionary<string, BusTimetable> busTimetableByKey = await busRepository.GetBusTimetableByJourneyKey(candidateKeysByKey.Values.SelectMany(x => x).Distinct().ToList());
                        foreach ((string journeyKey, List<string> candidateKeys) in candidateKeysByKey)
                        {
                            // The offsets are ordered nearest first, so the closest departure wins.
                            foreach (string candidateKey in candidateKeys)
                            {
                                if (!busTimetableByKey.TryGetValue(candidateKey, out BusTimetable? timetable))
                                    continue;

                                offsetMatchCount++;
                                FirstEstimation(journeyKey, busLocationByKey[journeyKey], timetable);
                                notFoundKey.Remove(journeyKey);
                                break;
                            }
                        }
                    }


                    // drop the data when the bus is no longer being tracked recently
                    var expiredKeys = new List<string>();
                    foreach ((string key, LiveBusJourney journey) in _journeyByKey)
                    {
                        if (_timeService.UkNowDateTime - journey.RecordedAtTime > _meta.DataRetentionPeriod)
                        {
                            expiredKeys.Add(key);
                        }
                    }

                    foreach (string key in expiredKeys)
                    {
                        _journeyByKey.Remove(key);
                    }

                    _transportDataStore.RefreshBusJourneys(_journeyByKey.ToFrozenDictionary());


                    _logger.LogInformation(
                        "Bus schedule estimation - {Matched} matched ({Cached} cached, {Exact} exact, {Offset} offset), {Unmatched} unmatched",
                        busLocationByKey.Count - notFoundKey.Count, cachedMatchCount, exactMatchCount, offsetMatchCount, notFoundKey.Count);

                    _logger.LogInformation("Bus schedule estimation completed in {Elapsed}s", stopwatch.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bus schedule estimation failed");
                }
            }
        }

        private void FirstEstimation(string tripScheduleKey, BusLocation busLocation, BusTimetable timetable)
        {
            (int? lastSeenSequence, int scheduleOffsetMinutes) = EstimateBusSchedule(busLocation, timetable.BusCallingPoints, null, 0);

            _journeyByKey[tripScheduleKey] = new LiveBusJourney
            {
                JourneyKey = busLocation.JourneyKey,
                RouteKey = timetable.RouteKey,
                BusCallingPoints = timetable.BusCallingPoints ?? [],
                LastArrivedStopSequence = lastSeenSequence,
                ScheduleOffsetMinutes = scheduleOffsetMinutes,
                Latitude = busLocation.Latitude,
                Longitude = busLocation.Longitude,
                Bearing = busLocation.Bearing,
                RecordedAtTime = busLocation.RecordedAtTime,
            };
        }

        private (int? LastSeenSequence, int ScheduleOffsetMinutes) EstimateBusSchedule(BusLocation busLocation, IReadOnlyList<BusCallingPoint>? busCallingPoints, int? lastSeenSequence, int scheduleOffsetMinutes)
        {
            IReadOnlyList<BusCallingPoint> callingPoints = busCallingPoints ?? [];
            if (callingPoints.Count == 0)
                return (lastSeenSequence, scheduleOffsetMinutes);

            // skip the first bus stop since the bus is there but not departure yet and not checking is time greater than schedule is because 1:00 is after 23:00 but TimeOnly thinks different
            // not checking last bus stop because if the bus staying at last stop, it is considered as delay though journey is finished
            int firstSequence = callingPoints[0].Sequence;
            int lastSequence = callingPoints[callingPoints.Count - 1].Sequence;
            IReadOnlyList<BusCallingPoint> remainingCallingPoints = callingPoints
                .Where(x => x.Sequence != firstSequence && x.Sequence != lastSequence)
                .ToList();

            // if the server start after the bus departure, it is possible that keep stuck at early stop and cannot update to new stop
            if (lastSeenSequence is not null)
                remainingCallingPoints = callingPoints
                    .Where(x => x.Sequence >= lastSeenSequence && lastSeenSequence + 5 >= x.Sequence) // prevent sequence jump from 5 to 40 cause it is a round trip
                    .ToList();

            // Measured against when the feed recorded the position, not against now
            TimeOnly recordedTime = TimeOnly.FromDateTime(busLocation.RecordedAtTime);
            foreach (BusCallingPoint callingPoint in remainingCallingPoints)
            {
                if (!_transportDataStore.StopById.TryGetValue(callingPoint.BusStopId, out Stop? busStop) || busStop is null)
                    continue;

                // ~50 m radius
                if (Math.Abs(busStop.Latitude - busLocation.Latitude) < 0.00045m && Math.Abs(busStop.Longitude - busLocation.Longitude) < 0.00065m)
                {
                    // ScheduledTime is measured from the operating day's midnight and can run past 24 hours, so it is
                    // brought back to a wall clock before being compared with one.
                    TimeSpan scheduledOnTheClock = TimeSpan.FromTicks(callingPoint.ScheduledTime.Ticks % TimeSpan.TicksPerDay);

                    // taken the short way round the clock, so a bus due 23:58 and seen at 00:02 is four minutes late rather than 1436 early.
                    int minutes = (int)(scheduledOnTheClock - recordedTime.ToTimeSpan()).TotalMinutes;

                    if (minutes > 720)
                        minutes -= 1440;

                    if (minutes < -720)
                        minutes += 1440;

                    return (callingPoint.Sequence, minutes);
                }
            }

            return (lastSeenSequence, scheduleOffsetMinutes);
        }


        // Bus:Schedule:Meta.
        public sealed record ScheduleMetaOptions
        {
            // Furthest a reported departure is shifted, in minutes, when nothing matched it exactly.
            public required int MaxDepartureOffsetMinutes { get; init; }

            // How long a journey is kept after it was last seen in the location feed.
            public required TimeSpan DataRetentionPeriod { get; init; }
        }
    }
}
