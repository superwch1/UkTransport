using Backend.Models;

namespace Backend
{
    public class TransportDataStore
    {
        private IReadOnlyList<BusLocation> _busLocations = [];

        public void RefreshBusLocations(IReadOnlyList<BusLocation> busLocations)
        {
            Interlocked.Exchange(ref _busLocations, busLocations);
        }

        public IReadOnlyList<BusLocation> GetBusLocations()
        {
            return _busLocations;
        }
    }
}
