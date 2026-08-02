using Backend.Models;
using Backend.Services;

namespace Backend.Repositories
{
    public class StopRepository
    {
        private readonly TransportDataStore _transportDataStore;
        private readonly TimeService _timeService;

        public StopRepository(TransportDataStore transportDataStore, TimeService timeService)
        {
            _transportDataStore = transportDataStore;
            _timeService = timeService;
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
