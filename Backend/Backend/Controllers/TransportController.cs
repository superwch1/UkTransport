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


        //[HttpGet("[controller]/bus/stops")]
        //public IActionResult BusStops([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        //{
        //    IReadOnlyList<Stop> busStops = _busRepository.GetBusStops(north, south, east, west);
        //    if (busStops.Count > 300)
        //        return Success(StatusCodes.Status200OK, response: Array.Empty<Stop>().ToBusStopsResponse(), message: "Zoom in to show bus stops");

        //    return Success(StatusCodes.Status200OK, response: busStops.ToBusStopsResponse());
        //}


        [HttpGet("[controller]/bus/line/{lineName}/routes")]
        public async Task<IActionResult> BusRoute(string lineName)
        {
            IReadOnlyList<BusRoute> busRoutes = _busRepository.GetBusRoutesByLineName(lineName);
            return Success(StatusCodes.Status200OK, response: busRoutes.ToBusRoutesResponse());
        }


        [HttpGet("[controller]/bus/routes/timetables")]
        public async Task<IActionResult> BusRoutesTimetables([FromQuery] IEnumerable<string> routeKeys)
        {
            if (routeKeys.Count() > 10)
                return BadRequest("That is a lot of ROUTESSS");

            Dictionary<string, List<BusTimetableItemResponse>> busTimetablesByPatternKey = [];
            foreach (string routeKey in routeKeys)
            {
                Dictionary<string, List<BusTimetableItemResponse>> routeTimetables = await _busRepository.GetBusTimetablesByRouteKey(routeKey);
                foreach ((string stopPatternKey, List<BusTimetableItemResponse> timetables) in routeTimetables)
                {
                    busTimetablesByPatternKey[stopPatternKey] = timetables;
                }
            }

            return Success(StatusCodes.Status200OK, response: busTimetablesByPatternKey.ToBusTimetablesResponse(_timeService.UkNowDateTime));
        }


        [HttpGet("[controller]/bus/route/{routeKey}/journeys")]
        public async Task<IActionResult> BusRouteJourneys(string routeKey)
        {
            IReadOnlyList<LiveBusJourney> busJourneys = _busRepository.GetLiveBusJourneysByRouteKey(routeKey);
            return Success(StatusCodes.Status200OK, response: busJourneys.ToLiveBusJourneysResponse());
        }
    }
}
