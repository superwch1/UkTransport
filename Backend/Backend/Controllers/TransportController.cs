using Backend.Extensions;
using Backend.Models;
using Backend.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [ApiController]
    public class TransportController : ApiControllerBase
    {
        private readonly BusRepository _busRepository;
        private readonly StopRepository _stopRepository;

        public TransportController(BusRepository busRepository, StopRepository stopRepository)
        {
            _busRepository = busRepository;
            _stopRepository = stopRepository;
        }


        [HttpGet("[controller]/bus/stops")]
        public IActionResult BusStops([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        {
            IReadOnlyList<Stop> busStops = _busRepository.GetBusStops(north, south, east, west);
            if (busStops.Count > 300)
                return Success(StatusCodes.Status200OK, response: Array.Empty<Stop>().ToBusStopsResponse(), message: "Zoom in to show bus stops");

            return Success(StatusCodes.Status200OK, response: busStops.ToBusStopsResponse());
        }


        [HttpGet("[controller]/bus/journeys/{journeyKey}")]
        public async Task<IActionResult> BusJourney(string journeyKey)
        {
            BusJourney? busJourney = _busRepository.GetBusJourneyByKey(journeyKey);
            if (busJourney == null)
                return Success(StatusCodes.Status200OK, message: "Failed to find the bus");

            return Success(StatusCodes.Status200OK, response: busJourney.ToBusJourneyResponse(_stopRepository.GetStop));
        }

        [HttpGet("[controller]/bus/lines/{lineName}/routes")]
        public async Task<IActionResult> BusRoute(string lineName)
        {
            IReadOnlyList<BusRoute> busRoutes = _busRepository.GetBusRoutes(lineName).Take(100).ToList();
            if (busRoutes.Count == 0)
                return Success(StatusCodes.Status200OK, response: Array.Empty<BusRoute>().ToBusRoutesResponse(), message: "Failed to find the bus");

            return Success(StatusCodes.Status200OK, response: busRoutes.ToBusRoutesResponse());
        }

        [HttpGet("[controller]/routes/{routeKey}/journeys")]
        public async Task<IActionResult> GetBusJourneysByRoute(string lineName)
        {
            IReadOnlyList<BusRoute> busRoutes = _busRepository.GetBusRoutes(lineName).Take(100).ToList();
            if (busRoutes.Count == 0)
                return Success(StatusCodes.Status200OK, response: Array.Empty<BusRoute>().ToBusRoutesResponse(), message: "Failed to find the bus");

            return Success(StatusCodes.Status200OK, response: busRoutes.ToBusRoutesResponse());
        }


        /*
        [HttpGet("[controller]/bus/{journeyKey}/location/")]
        public IActionResult BusLocation(string journeyKey)
        {
            var busJourney = _busRepository.GetBusJourneyByKey(journeyKey);
            if (busJourney == null)
                return Success(StatusCodes.Status200OK, message: "Failed to find the bus");

            return Success(StatusCodes.Status200OK, response: busJourney.ToBusLocationItemResponse());
        }


        [HttpGet("[controller]/bus/locations")]
        public IActionResult BusLocations([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        {
            var busJourneys = _busRepository.GetBusJourneys(north, south, east, west);
            if (busJourneys.Count > 300)
                return Success(StatusCodes.Status200OK, response: Array.Empty<BusJourney>().ToBusLocationsResponse(), message: "Zoom in to show bus real time location");

            return Success(StatusCodes.Status200OK, response: busJourneys.ToBusLocationsResponse());
        }*/
    }
}
