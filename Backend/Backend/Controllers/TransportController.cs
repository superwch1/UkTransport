using Backend.Extensions;
using Backend.Models;
using Backend.Repositories;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [ApiController]
    public class TransportController : ApiControllerBase
    {
        private readonly BusRepository _busRepository;
        private readonly StopRepository _stopRepository;
        private readonly TimeService _timeService;

        public TransportController(BusRepository busRepository, StopRepository stopRepository, TimeService timeService)
        {
            _busRepository = busRepository;
            _stopRepository = stopRepository;
            _timeService = timeService;
        }


        [HttpGet("[controller]/bus/stops")]
        public IActionResult BusStops([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        {
            IReadOnlyList<Stop> busStops = _busRepository.GetBusStops(north, south, east, west);
            if (busStops.Count > 300)
                return Success(StatusCodes.Status200OK, response: Array.Empty<Stop>().ToBusStopsResponse(), message: "Zoom in to show bus stops");

            return Success(StatusCodes.Status200OK, response: busStops.ToBusStopsResponse());
        }


        [HttpGet("[controller]/bus/line/{lineName}/routes")]
        public async Task<IActionResult> BusRoute(string lineName)
        {
            IReadOnlyList<BusRoute> busRoutes = _busRepository.GetBusRoutesByLineName(lineName);
            return Success(StatusCodes.Status200OK, response: busRoutes.ToBusRoutesResponse());
        }


        [HttpGet("[controller]/bus/route/{routeKey}/timetables")]
        public async Task<IActionResult> BusRouteTimetables(string routeKey)
        {
            IReadOnlyList<(DateOnly Date, IReadOnlyList<BusTimetable> BusTimetables)> busTimetablesByDate = await _busRepository.GetBusTimetablesByRouteKey(routeKey);
            return Success(StatusCodes.Status200OK, response: busTimetablesByDate.ToBusTimetablesResponse(_timeService.UkNowDateTime, _stopRepository.GetStopById));
        }


        [HttpGet("[controller]/bus/route/{routeKey}/journeys")]
        public async Task<IActionResult> BusRouteJourneys(string routeKey)
        {
            IReadOnlyList<LiveBusJourney> busJourneys = _busRepository.GetLiveBusJourneysByRouteKey(routeKey);
            return Success(StatusCodes.Status200OK, response: busJourneys.ToLiveBusJourneysResponse());
        }
    }
}
