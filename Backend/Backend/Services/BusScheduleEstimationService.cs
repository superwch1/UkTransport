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

        private readonly int _batchSize = 500;
        private readonly TimeSpan _scheduleEstimateRetentionPeriod = TimeSpan.FromHours(2);
        private readonly Dictionary<string, BusScheduleEstimate> _scheduleEstimateByKey = new Dictionary<string, BusScheduleEstimate>();

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

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    foreach (BusLocation[] batch in busLocationByKey.Values.Chunk(_batchSize))
                    {
                        using IServiceScope scope = _serviceScopeFactory.CreateScope();
                        BusRepository busRepository = scope.ServiceProvider.GetRequiredService<BusRepository>();

                        var busRouteByKey = await busRepository.GetBusRoutes(batch.Select(x => x.OriginDepartureKey));
                        foreach ((string originDepartureKey, IReadOnlyList<BusCallingPoint> callingPoints) in busRouteByKey)
                        {
                            if (!busLocationByKey.TryGetValue(originDepartureKey, out BusLocation? busLocation) || busLocation is null)
                                continue;

                            // skip the first bus stop since the bus is there but not departure yet
                            // reason not checking is time greater than schedule is because 1:00 is after 23:00 but TimeOnly thinks different
                            IReadOnlyList<BusCallingPoint> remainingCallingPoints = callingPoints
                                .Where(x => x.Sequence != 0)
                                .ToList();

                            // if the server start after the bus departure, it is possible that keep stuck at early stop and cannot update to new stop
                            if (_scheduleEstimateByKey.TryGetValue(originDepartureKey, out BusScheduleEstimate? previousEstimate) && previousEstimate is not null)
                                remainingCallingPoints = callingPoints
                                    .Where(x => x.Sequence >= previousEstimate.Sequence && previousEstimate.Sequence + 5 >= x.Sequence) // prevent sequence jump from 5 to 40 cause it is a round trip
                                    .ToList();

                            foreach (BusCallingPoint callingPoint in remainingCallingPoints)
                            {
                                if (!_transportDataStore.StopById.TryGetValue(callingPoint.BusStopId, out Stop? busStop) || busStop is null)
                                    continue;

                                TimeOnly? scheduledTime = callingPoint.ArrivalTime ?? callingPoint.DepartureTime;
                                if (scheduledTime is null)
                                    continue;

                                // ~50 m radius
                                if (Math.Abs(busStop.Latitude - busLocation.Latitude) < 0.00045m && Math.Abs(busStop.Longitude - busLocation.Longitude) < 0.00065m)
                                {
                                    // prevent wrap around in TimeOnly data structure (18:05 - 18:07 = 23h58m = 1438 minutes)
                                    int scheduleOffsetMinutes = Math.Abs((_timeService.UkNowTimeOnly.ToTimeSpan() - scheduledTime.Value.ToTimeSpan()).Minutes);
                                    _scheduleEstimateByKey[originDepartureKey] = new BusScheduleEstimate
                                    {
                                        Sequence = callingPoint.Sequence,
                                        ScheduleOffsetMinutes = scheduleOffsetMinutes,
                                        CalculatedAt = _timeService.UkNowDateTimeOffset,
                                    };
                                    break;
                                }
                            }
                        }
                    }

                    var expiredKeys = new List<string>();
                    foreach ((string key, BusScheduleEstimate estimate) in _scheduleEstimateByKey)
                    {
                        if (_timeService.UkNowDateTimeOffset - estimate.CalculatedAt > _scheduleEstimateRetentionPeriod)
                        {
                            expiredKeys.Add(key);
                        }
                    }

                    foreach (string key in expiredKeys)
                    {
                        _scheduleEstimateByKey.Remove(key);
                    }

                    _transportDataStore.RefreshBusScheduleEstimate(_scheduleEstimateByKey.ToFrozenDictionary());
                    _logger.LogInformation("Bus schedule estimation completed in {Elapsed}s", stopwatch.Elapsed.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bus schedule estimation pass failed");
                }
            }
        }
    }
}
