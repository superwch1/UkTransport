using Backend.Models;
using Backend.Services;

namespace Backend.Repositories
{
    public class StopRepository
    {
        private readonly TransportDataStore _transportDataStore;

        public StopRepository(TransportDataStore transportDataStore)
        {
            _transportDataStore = transportDataStore;
        }


        public Stop? GetStopById(string busStopId)
        {
            if (_transportDataStore.StopById.TryGetValue(busStopId, out Stop? stop) && stop is not null)
                return stop;

            return null;
        }

        public bool IsStopFinishedImport()
        {
            return _transportDataStore.StopById.Count > 0;
        }
    }
}
