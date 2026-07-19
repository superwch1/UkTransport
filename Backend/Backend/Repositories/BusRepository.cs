using Backend.Models;

namespace Backend.Repositories
{
    public class BusRepository
    {
        private readonly TransportDataStore _transportDataStore;

        public BusRepository(TransportDataStore transportDataStore)
        {
            _transportDataStore = transportDataStore;
        }


        public BusLocation? GetBusLocationById(string id)
        {
            return _transportDataStore.GetBusLocations()
                .FirstOrDefault(busLocation => busLocation.Id == id);
        }


        public IReadOnlyList<BusLocation> GetBusLocations(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore.GetBusLocations()
                .Where(busLocation => busLocation.Latitude <= north && busLocation.Latitude >= south && busLocation.Longitude <= east && busLocation.Longitude >= west)
                .ToList();
        }


        public IReadOnlyList<BusStop> GetBusStops(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore.GetBusStops()
                .Where(busStop => busStop.Latitude <= north && busStop.Latitude >= south && busStop.Longitude <= east && busStop.Longitude >= west)
                .ToList();
        }
    }
}
