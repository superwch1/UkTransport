using Backend.Models;
using System.Collections.Frozen;
using System.Threading.Channels;

namespace Backend
{
    public class TransportDataStore
    {
        private FrozenDictionary<string, BusJourney> _busJourneyByKey = FrozenDictionary.Create<string, BusJourney>();
        public FrozenDictionary<string, BusJourney> BusJourneyByKey => _busJourneyByKey;


        private FrozenDictionary<string, Stop> _stopsById = FrozenDictionary.Create<string, Stop>();
        public FrozenDictionary<string, Stop> StopById => _stopsById;


        public FrozenDictionary<string, IReadOnlyList<BusRoute>> _busRouteByLineName = FrozenDictionary.Create<string, IReadOnlyList<BusRoute>>();
        public FrozenDictionary<string, IReadOnlyList<BusRoute>> BusRouteByLineName => _busRouteByLineName;



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
            await _busLocationByKeyChannel.Writer.WriteAsync(busLocationByKey);
        }

        public void RefreshStops(Dictionary<string, Stop> stops)
        {
            Interlocked.Exchange(ref _stopsById, stops.Values.ToFrozenDictionary(x => x.Id, x => x));
        }

        public void RefreshBusJourneys(FrozenDictionary<string, BusJourney> journeys)
        {
            Interlocked.Exchange(ref _busJourneyByKey, journeys);
        }
    }
}
