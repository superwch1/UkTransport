using Backend.Models;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading.Channels;

namespace Backend
{
    public class TransportDataStore
    {
        private FrozenDictionary<string, LiveBusJourney> _liveBusJourneyByKey = FrozenDictionary.Create<string, LiveBusJourney>();
        public FrozenDictionary<string, LiveBusJourney> LiveBusJourneyByKey => _liveBusJourneyByKey;


        private FrozenDictionary<string, Stop> _stopsById = FrozenDictionary.Create<string, Stop>();
        public FrozenDictionary<string, Stop> StopById => _stopsById;


        private ImmutableArray<BusRoute> _busRoutes = [];
        public ImmutableArray<BusRoute> BusRoutes => _busRoutes;


        private readonly Channel<IReadOnlyDictionary<string, BusLocation>> _busLocationByKeyChannel =
            Channel.CreateBounded<IReadOnlyDictionary<string, BusLocation>>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        public async ValueTask<IReadOnlyDictionary<string, BusLocation>> ReadBusLocationAsync()
        {
            return await _busLocationByKeyChannel.Reader.ReadAsync();
        }              

        public async Task RefreshBusLocations(IReadOnlyDictionary<string, BusLocation> busLocationByKey)
        {            
            await _busLocationByKeyChannel.Writer.WriteAsync(busLocationByKey);
        }

        public void RefreshStops(FrozenDictionary<string, Stop> stops)
        {
            Interlocked.Exchange(ref _stopsById, stops.Values.ToFrozenDictionary(x => x.Id, x => x));
        }

        public void RefreshBusJourneys(FrozenDictionary<string, LiveBusJourney> journeys)
        {
            Interlocked.Exchange(ref _liveBusJourneyByKey, journeys);
        }

        public void RefreshBusRoutes(ImmutableArray<BusRoute> routes)
        {
            ImmutableInterlocked.InterlockedExchange(ref _busRoutes, routes);
        }
    }
}
