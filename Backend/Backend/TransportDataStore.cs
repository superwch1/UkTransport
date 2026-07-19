using Backend.Models;
using System.Collections.Frozen;

namespace Backend
{
    public class TransportDataStore
    {
        private IReadOnlyList<BusLocation> _busLocations = [];

        private IReadOnlyList<BusStop> _busStops = [];
        private FrozenDictionary<string, BusStop> _busStopsById = FrozenDictionary.Create<string, BusStop>();

        public void RefreshBusLocations(IReadOnlyList<BusLocation> busLocations)
        {
            Interlocked.Exchange(ref _busLocations, busLocations);
        }

        public IReadOnlyList<BusLocation> GetBusLocations()
        {
            return _busLocations;
        }

        public void SetBusStops(Dictionary<string, BusStop> busStops)
        {
            Interlocked.Exchange(ref _busStops, busStops.Values.ToList());
            Interlocked.Exchange(ref _busStopsById, busStops.ToFrozenDictionary());
        }

        public IReadOnlyList<BusStop> GetBusStops()
        {
            return _busStops;
        }
    }
}
