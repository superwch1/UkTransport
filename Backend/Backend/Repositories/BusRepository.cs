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
                .FirstOrDefault(bus => bus.Id == id);
        }


        public IReadOnlyList<BusLocation> GetBusLocations(decimal north, decimal south, decimal east, decimal west)
        {
            return _transportDataStore.GetBusLocations()
                .Where(bus => bus.Latitude <= north && bus.Latitude >= south && bus.Longitude <= east && bus.Longitude >= west)
                .ToList();
        }
    }
}
