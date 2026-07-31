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

                    Stopwatch stopwatch = Stopwatch.StartNew();

                    // First: search for the journey from memory
                    foreach (BusLocation busLocation in busLocationByKey.Values)
                    {
                        if (!_journeyByKey.TryGetValue(busLocation.TripScheduleKey, out BusJourney? journeyState))
                            continue;

                        ReestimateSchedule(busLocation.TripScheduleKey, busLocation, journeyState);
                        notFoundKey.Remove(busLocation.TripScheduleKey);
                    }
                    int cachedMatchCount = busLocationByKey.Count - notFoundKey.Count;


                    // Second: search for the journey in database using departure-origin-destination key
                    List<BusLocation> uncachedBusLocations = notFoundKey.Select(x => busLocationByKey[x]).ToList();
                    foreach (BusLocation[] batch in uncachedBusLocations.Chunk(_batchSize))
                    {
                        using IServiceScope scope = _serviceScopeFactory.CreateScope();
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                        IReadOnlyDictionary<string, BusTimetable> busTimetableByKey = await busRepository.GetBusTimetableByKey(batch.Select(x => x.TripScheduleKey).ToList());

                        notFoundKey.ExceptWith(busTimetableByKey.Keys);

                        foreach ((string tripScheduleKey, BusTimetable timetable) in busTimetableByKey)
                        {
                            if (!busLocationByKey.TryGetValue(tripScheduleKey, out BusLocation? busLocation) || busLocation is null)
                                continue;

                            EstimateSchedule(tripScheduleKey, busLocation, timetable);
                        }
                    }
                    int exactMatchCount = busLocationByKey.Count - cachedMatchCount - notFoundKey.Count;


                    // Third: shift minutes for each departure-origin-destination key then perform search in database
                    List<BusLocation> unmatchedBusLocations = notFoundKey.Select(x => busLocationByKey[x]).ToList();
                    foreach (BusLocation[] batch in unmatchedBusLocations.Chunk(_batchSize))
                    {
                        using IServiceScope scope = _serviceScopeFactory.CreateScope();
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                        Dictionary<string, List<string>> candidateKeysByKey = batch.ToDictionary(
                            x => x.TripScheduleKey,
                            x => _departureOffsetMinutes
                                // it is build from -1, 1, -2, 2, -3, 3 ...
                                .Select(offset => BusTimeTableExtension.BuildTripScheduleKey(x.OriginAimedDepartureTime.AddMinutes(offset), x.OriginRef, x.DestinationRef))
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

                                EstimateSchedule(tripScheduleKey, busLocationByKey[tripScheduleKey], timetable);
                                notFoundKey.Remove(tripScheduleKey);
                                break;
                            }
                        }
                    }
                    int offsetMatchCount = busLocationByKey.Count - exactMatchCount - notFoundKey.Count;


                    // Counted per operator rather than in total, because a raw number of failures only ranks operators by fleet size
                    var resultCountByOperator = new Dictionary<string, (int Matched, int Unmatched)>();
                    foreach (BusLocation busLocation in busLocationByKey.Values)
                    {
                        resultCountByOperator.TryGetValue(busLocation.OperatorRef, out (int Matched, int Unmatched) resultCount);

                        if (notFoundKey.Contains(busLocation.TripScheduleKey))
                        {
                            resultCount.Unmatched++;
                        }
                        else
                        {
                            resultCount.Matched++;
                        }

                        resultCountByOperator[busLocation.OperatorRef] = resultCount;
                    }

                    foreach ((string operatorRef, (int matched, int unmatched)) in resultCountByOperator.Where(x => x.Value.Unmatched > 0).OrderByDescending(x => x.Value.Unmatched))
                    {
                        int total = matched + unmatched;
                        Console.WriteLine($"{operatorRef} - {matched}/{total} matched ({(double)unmatched / total:P0} failed)");
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


                    // Only journeys whose bus has actually been seen at a calling point carry an estimate to publish.
                    _transportDataStore.RefreshBusJourneys(_journeyByKey.ToFrozenDictionary());


                    _logger.LogInformation(
                        "Bus schedule estimation completed in {Elapsed}s - {Total} live: {Cached} cached, {Exact} exact, {Offset} by departure offset, {Unmatched} unmatched ({Journeys} journeys held)",
                        stopwatch.Elapsed.TotalSeconds, busLocationByKey.Count, cachedMatchCount, exactMatchCount, offsetMatchCount, notFoundKey.Count, _journeyByKey.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bus schedule estimation failed");
                }
            }
        }

        private void EstimateSchedule(string tripScheduleKey, BusLocation busLocation, BusTimetable timetable)
        {
            (int? lastSeenSequence, int scheduleOffsetMinutes) = LocateBus(busLocation, timetable.BusCallingPoints, null, 0);

            _journeyByKey[tripScheduleKey] = new BusJourney
            {
                Id = timetable.Id,
                DatasetId = timetable.DatasetId,
                OperatorId = busLocation.OperatorRef,
                OperatorName = timetable.OperatorName,
                LineName = busLocation.PublishedLineName,
                OriginName = busLocation.OriginName,
                DestinationName = busLocation.DestinationName,
                DirectionRef = busLocation.DirectionRef,
                ScheduledDayOffset = timetable.ScheduledDayOffset,
                TripScheduleKey = timetable.TripScheduleKey,
                OriginBusStopId = busLocation.OriginRef,
                OriginAimedDepartureTime = busLocation.OriginAimedDepartureTime,
                DestinationBusStopId = busLocation.DestinationRef,
                DestinationAimedArrivalTime = busLocation.DestinationAimedArrivalTime,
                BusCallingPoints = timetable.BusCallingPoints,
                LastSeenSequence = lastSeenSequence,
                ScheduleOffsetMinutes = scheduleOffsetMinutes,
                Latitude = busLocation.Latitude,
                Longitude = busLocation.Longitude,
                Bearing = busLocation.Bearing,
                RecordedAtTime = busLocation.RecordedAtTime,
            };
        }

        private void ReestimateSchedule(string tripScheduleKey, BusLocation busLocation, BusJourney busJourney)
        {
            (int? lastSeenSequence, int scheduleOffsetMinutes) = LocateBus(busLocation, busJourney.BusCallingPoints, busJourney.LastSeenSequence, busJourney.ScheduleOffsetMinutes);

            _journeyByKey[tripScheduleKey] = busJourney with
            {
                LastSeenSequence = lastSeenSequence,
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
