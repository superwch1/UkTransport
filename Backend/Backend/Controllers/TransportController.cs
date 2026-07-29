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
        private readonly TransportDataStore _transportDataStore;

        public TransportController(BusRepository busRepository, TransportDataStore transportDataStore)
        {
            _busRepository = busRepository;
            _transportDataStore = transportDataStore;
        }


        [HttpGet("[controller]/bus/stop/{id}/timetables")]
        public async Task<IActionResult> BusStopTimetables(string id)
        {
            IReadOnlyDictionary<string, TimeOnly> busStopTimeTables = await _busRepository.GetBusStopTimetable(id, DateTime.Now, false);
            return Success(StatusCodes.Status200OK, response: busStopTimeTables.ToBusCallingPointsResponse());
        }


        [HttpGet("[controller]/bus/stops")]
        public IActionResult BusStops([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        {
            IReadOnlyList<Stop> busStops = _busRepository.GetBusStops(north, south, east, west);
            if (busStops.Count > 300)
                return Success(StatusCodes.Status200OK, response: Array.Empty<Stop>().ToBusStopsResponse(), message: "Zoom in to show bus stops");

            return Success(StatusCodes.Status200OK, response: busStops.ToBusStopsResponse());
        }


        [HttpGet("[controller]/bus/{tripScheduleKey}/route")]
        public async Task<IActionResult> BusRoute(string tripScheduleKey)
        {
            IReadOnlyList<BusCallingPoint> callingPoints = await _busRepository.GetBusRoute(tripScheduleKey);
            return Success(StatusCodes.Status200OK, response: callingPoints.ToBusRoutesResponse(_transportDataStore.StopById));
        }


        [HttpGet("[controller]/bus/{tripScheduleKey}/location/")]
        public IActionResult BusLocation(string tripScheduleKey)
        {
            var busLocation = _busRepository.GetBusLocationById(tripScheduleKey);
            if (busLocation == null)
                return Success(StatusCodes.Status200OK, message: "Failed to find the bus");

            return Success(StatusCodes.Status200OK, response: busLocation.ToBusLocationItemResponse(_transportDataStore.BusScheduleEstimateByKey));
        }


        [HttpGet("[controller]/bus/locations")]
        public IActionResult BusLocations([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        {
            var busLocations = _busRepository.GetBusLocations(north, south, east, west);
            if (busLocations.Count > 300)
                return Success(StatusCodes.Status200OK, response: Array.Empty<BusLocation>().ToBusLocationsResponse(_transportDataStore.BusScheduleEstimateByKey), message: "Zoom in to show bus real time location");

            return Success(StatusCodes.Status200OK, response: busLocations.ToBusLocationsResponse(_transportDataStore.BusScheduleEstimateByKey));
        }
    }
}
