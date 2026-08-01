using Backend.Models;
using Backend.Repositories;
using System.Collections.Frozen;
using System.Diagnostics;

namespace Backend.Services
{
    public class BusScheduleEstimationService : BackgroundService
    {
        private readonly TransportDataStore _transportDataStore;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly TimeService _timeService;
        private readonly ILogger _logger;

        // Minutes to shift a reported departure by when nothing matched it exactly: -1, 1, -2, 2 and so on out to -10, 10
        private static readonly int[] _departureOffsetMinutes = [.. Enumerable.Range(1, 10).SelectMany(x => new[] { -x, x })];
        private readonly int _batchSize = 500;

        // Debugging only: which operator's unmatched buses to dump, and how many of them
        private const string _debugOperatorRef = "TFLO";
        private const int _debugSampleSize = 10;

        private readonly TimeSpan _dataRetentionPeriod = TimeSpan.FromMinutes(30);
        private readonly Dictionary<string, BusJourney> _journeyByKey = [];

        public BusScheduleEstimationService(TransportDataStore transportDataStore, TimeService timeService, IServiceScopeFactory serviceScopeFactory, ILogger<BusScheduleEstimationService> logger)
        {
            _transportDataStore = transportDataStore;
            _timeService = timeService;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }


        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    FrozenDictionary<string, BusLocation> busLocationByKey = await _transportDataStore.ReadBusLocationAsync();
                    HashSet<string> notFoundKey = busLocationByKey.Keys.ToHashSet();

                    // early return since the bus stop is not finished imported yet
                    using IServiceScope stopScope = _serviceScopeFactory.CreateScope();
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
                        if (!_journeyByKey.TryGetValue(busLocation.TripJourneyKey, out BusJourney? journeyState))
                            continue;

                        cachedMatchCount++;
                        ReestimateSchedule(busLocation.TripJourneyKey, busLocation, journeyState);
                        notFoundKey.Remove(busLocation.TripJourneyKey);
                    }


                    // Second: search for the journey in database using departure-origin-destination key
                    List<BusLocation> uncachedBusLocations = notFoundKey.Select(x => busLocationByKey[x]).ToList();
                    int exactMatchCount = 0;
                    foreach (BusLocation[] batch in uncachedBusLocations.Chunk(_batchSize))
                    {
                        using IServiceScope scope = _serviceScopeFactory.CreateScope();
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                        IReadOnlyDictionary<string, BusTimetable> busTimetableByKey = await busRepository.GetBusTimetableByKey(batch.Select(x => x.TripJourneyKey).ToList());

                        notFoundKey.ExceptWith(busTimetableByKey.Keys);

                        foreach ((string tripJourneyKey, BusTimetable timetable) in busTimetableByKey)
                        {
                            if (!busLocationByKey.TryGetValue(tripJourneyKey, out BusLocation? busLocation) || busLocation is null)
                                continue;

                            exactMatchCount++;
                            FirstEstimateSchedule(tripJourneyKey, busLocation, timetable);
                        }
                    }


                    // Third: shift minutes for each departure-origin-destination key then perform search in database
                    List<BusLocation> unmatchedBusLocations = notFoundKey.Select(x => busLocationByKey[x]).ToList();
                    int offsetMatchCount = 0;
                    foreach (BusLocation[] batch in unmatchedBusLocations.Chunk(_batchSize))
                    {
                        using IServiceScope scope = _serviceScopeFactory.CreateScope();
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                        IReadOnlyDictionary<string, List<string>> candidateKeysByKey = batch.ToDictionary(
                            x => x.TripJourneyKey,
                            x => _departureOffsetMinutes
                                // it is build from -1, 1, -2, 2, -3, 3 ...
                                .Select(offset => BusTimeTableExtension.BuildTripJourneyKey(x.LineName, x.OriginAimedDepartureTime.AddMinutes(offset), x.OriginBusStopId, x.DestinationBusStopId))
                                .ToList());

                        IReadOnlyDictionary<string, BusTimetable> busTimetableByKey = await busRepository.GetBusTimetableByKey(candidateKeysByKey.Values.SelectMany(x => x).Distinct().ToList());
                        if (busTimetableByKey.Count == 0)
                            continue;

                        foreach ((string tripScheduleKey, List<string> candidateKeys) in candidateKeysByKey)
                        {
                            // The offsets are ordered nearest first, so the closest departure wins.
                            foreach (string candidateKey in candidateKeys)
                            {
                                if (!busTimetableByKey.TryGetValue(candidateKey, out BusTimetable? timetable))
                                    continue;

                                offsetMatchCount++;
                                FirstEstimateSchedule(tripScheduleKey, busLocationByKey[tripScheduleKey], timetable);
                                notFoundKey.Remove(tripScheduleKey);
                                break;
                            }
                        }
                    }


                    // drop the data when the bus is no longer being tracked recently
                    var expiredKeys = new List<string>();
                    foreach ((string key, BusJourney journeyState) in _journeyByKey)
                    {
                        if (_timeService.UkNowDateTime - journeyState.RecordedAtTime > _dataRetentionPeriod)
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
                        "Bus schedule estimation - {Valid} valid ({Cached} cached, {Exact} exact, {Offset} offset), {Unmatched} unmatched",
                        busLocationByKey.Count, cachedMatchCount, exactMatchCount, offsetMatchCount, notFoundKey.Count);

                    _logger.LogInformation("Bus schedule estimation completed in {Elapsed}s", stopwatch.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bus schedule estimation failed");
                }
            }
        }

        private void FirstEstimateSchedule(string tripScheduleKey, BusLocation busLocation, BusTimetable timetable)
        {
            (int? lastSeenSequence, int scheduleOffsetMinutes) = LocateBus(busLocation, timetable.BusCallingPoints, null, 0);

            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            StopRepository stopRepository = scope.ServiceProvider.GetRequiredService<StopRepository>();

            Stop? originBusStop = stopRepository.GetStop(busLocation.OriginBusStopId);
            Stop? destinationBusStop = stopRepository.GetStop(busLocation.DestinationBusStopId);

            _journeyByKey[tripScheduleKey] = new BusJourney
            {
                DatasetId = timetable.DatasetId,
                OperatorId = busLocation.OperatorId,
                OperatorName = timetable.OperatorName,
                LineName = busLocation.LineName,
                OriginName = originBusStop is not null ? originBusStop.Name : busLocation.OriginBusStopId,
                DestinationName = destinationBusStop is not null ? destinationBusStop.Name : busLocation.DestinationBusStopId,
                Direction = busLocation.Direction,
                ScheduledDayOffset = timetable.ScheduledDayOffset,
                TripJourneyKey = busLocation.TripJourneyKey,
                OriginBusStopId = busLocation.OriginBusStopId,
                OriginAimedDepartureTime = busLocation.OriginAimedDepartureTime,
                DestinationBusStopId = busLocation.DestinationBusStopId,
                DestinationAimedArrivalTime = busLocation.DestinationAimedArrivalTime,
                BusCallingPoints = timetable.BusCallingPoints ?? [],
                LastArrivedStopSequence = lastSeenSequence,
                ScheduleOffsetMinutes = scheduleOffsetMinutes,
                Latitude = busLocation.Latitude,
                Longitude = busLocation.Longitude,
                Bearing = busLocation.Bearing,
                RecordedAtTime = busLocation.RecordedAtTime,
            };
        }

        private void ReestimateSchedule(string tripScheduleKey, BusLocation busLocation, BusJourney busJourney)
        {
            (int? lastSeenSequence, int scheduleOffsetMinutes) = LocateBus(busLocation, busJourney.BusCallingPoints, busJourney.LastArrivedStopSequence, busJourney.ScheduleOffsetMinutes);

            _journeyByKey[tripScheduleKey] = busJourney with
            {
                LastArrivedStopSequence = lastSeenSequence,
                ScheduleOffsetMinutes = scheduleOffsetMinutes,
                Latitude = busLocation.Latitude,
                Longitude = busLocation.Longitude,
                Bearing = busLocation.Bearing,
                RecordedAtTime = busLocation.RecordedAtTime,
            };
        }

        private (int? LastSeenSequence, int ScheduleOffsetMinutes) LocateBus(BusLocation busLocation, IReadOnlyList<BusCallingPoint>? busCallingPoints, int? lastSeenSequence, int scheduleOffsetMinutes)
        {
            IReadOnlyList<BusCallingPoint> callingPoints = busCallingPoints ?? [];
            if (callingPoints.Count == 0)
                return (lastSeenSequence, scheduleOffsetMinutes);

            // skip the first bus stop since the bus is there but not departure yet
            // reason not checking is time greater than schedule is because 1:00 is after 23:00 but TimeOnly thinks different
            // also not checking last because if the bus keep staying at last station may considered as delay although journey is finished
            int lastSequence = callingPoints[callingPoints.Count - 1].Sequence;
            IReadOnlyList<BusCallingPoint> remainingCallingPoints = callingPoints
                .Where(x => x.Sequence != 0 && x.Sequence != lastSequence)
                .ToList();

            // if the server start after the bus departure, it is possible that keep stuck at early stop and cannot update to new stop
            if (lastSeenSequence is not null)
                remainingCallingPoints = callingPoints
                    .Where(x => x.Sequence >= lastSeenSequence && lastSeenSequence + 5 >= x.Sequence) // prevent sequence jump from 5 to 40 cause it is a round trip
                    .ToList();

            // Measured against when the feed recorded the position, not against now. A reading can be up to ten minutes
            // old by the time it is read, and charging that delay to the bus would report it later than it ran.
            TimeOnly recordedTime = TimeOnly.FromDateTime(busLocation.RecordedAtTime);

            foreach (BusCallingPoint callingPoint in remainingCallingPoints)
            {
                if (!_transportDataStore.StopById.TryGetValue(callingPoint.BusStopId, out Stop? busStop) || busStop is null)
                    continue;

                // ~50 m radius
                if (Math.Abs(busStop.Latitude - busLocation.Latitude) < 0.00045m && Math.Abs(busStop.Longitude - busLocation.Longitude) < 0.00065m)
                {
                    return (callingPoint.Sequence, MinutesFromSchedule(callingPoint.ScheduledTime, recordedTime));
                }
            }

            return (lastSeenSequence, scheduleOffsetMinutes);
        }

        // How far off its scheduled time the bus was at that stop: positive late, negative early. Taken the short way
        // round the clock, so a bus due 23:58 and seen at 00:02 is four minutes late rather than 1436 early.
        private static int MinutesFromSchedule(TimeOnly scheduledTime, TimeOnly recordedTime)
        {
            int minutes = (int)(recordedTime.ToTimeSpan() - scheduledTime.ToTimeSpan()).TotalMinutes;

            if (minutes > 720)
                return minutes - 1440;

            if (minutes < -720)
                return minutes + 1440;

            return minutes;
        }
    }
}
