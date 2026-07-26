using Backend.Models;
using System.Collections.Frozen;
using System.Threading.Channels;

namespace Backend
{
    public class TransportDataStore
    {
        private FrozenDictionary<string, BusLocation> _busLocationByKey = FrozenDictionary.Create<string, BusLocation>();
        public FrozenDictionary<string, BusLocation> BusLocationByKey => _busLocationByKey;


        private FrozenDictionary<string, Stop> _stopsById = FrozenDictionary.Create<string, Stop>();
        public FrozenDictionary<string, Stop> StopById => _stopsById;


        private FrozenDictionary<string, BusScheduleEstimate> _busScheduleEstimatetByKey = FrozenDictionary.Create<string, BusScheduleEstimate>();
        public FrozenDictionary<string, BusScheduleEstimate> BusScheduleEstimateByKey => _busScheduleEstimatetByKey;


        private readonly Channel<FrozenDictionary<string, BusLocation>> _busLocationByKeyChannel =
            Channel.CreateBounded<FrozenDictionary<string, BusLocation>>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        public async ValueTask<FrozenDictionary<string, BusLocation>> ReadBusLocationAsync()
        {
            return await _busLocationByKeyChannel.Reader.ReadAsync();
        }

        public async Task RefreshBusLocations(FrozenDictionary<string, BusLocation> busLocationByKey)
        {
            Interlocked.Exchange(ref _busLocationByKey, busLocationByKey);
            await _busLocationByKeyChannel.Writer.WriteAsync(busLocationByKey);
        }

        public void RefreshStops(Dictionary<string, Stop> stops)
        {
            Interlocked.Exchange(ref _stopsById, stops.Values.ToFrozenDictionary(x => x.Id, x => x));
        }

        public void RefreshBusScheduleEstimate(FrozenDictionary<string, BusScheduleEstimate> busScheduleEstimateByKey)
        {
            Interlocked.Exchange(ref _busScheduleEstimatetByKey, busScheduleEstimateByKey);
        }
    }
}
