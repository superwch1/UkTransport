using Backend.Models;
using System.Collections.Frozen;

namespace Backend
{
    public class TransportDataStore
    {
        private FrozenDictionary<string, BusLocation> _busLocationByKey = FrozenDictionary.Create<string, BusLocation>();
        private FrozenDictionary<string, BusStop> _busStopsById = FrozenDictionary.Create<string, BusStop>();

        public void RefreshBusLocations(FrozenDictionary<string, BusLocation> busLocationByKey)
        {
            Interlocked.Exchange(ref _busLocationByKey, busLocationByKey);
        }

        public FrozenDictionary<string, BusLocation> BusLocationByKey
        {
            get { return _busLocationByKey; }
        }

        public void SetBusStops(Dictionary<string, BusStop> busStops)
        {
            Interlocked.Exchange(ref _busStopsById, busStops.Values.ToFrozenDictionary(x => x.Id, x => x));
        }

        public FrozenDictionary<string, BusStop> BusStopById
        {
            get { return _busStopsById; }
        }
    }
}
