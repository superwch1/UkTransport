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

        public TransportController(BusRepository busRepository)
        {
            _busRepository = busRepository;
        }

        [HttpGet("[controller]/bus/locations")]
        public IActionResult BusLocations([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        {
            var busLocations = _busRepository.GetBusLocations(north, south, east, west);
            if (busLocations.Count > 100)
                return Success(StatusCodes.Status200OK, response: Array.Empty<BusLocation>().ToBusLocationsResponse(), message: "Zoom in to show bus real time location");

            return Success(StatusCodes.Status200OK, response: busLocations.ToBusLocationsResponse());
        }


        [HttpGet("[controller]/bus/{id}/location/")]
        public IActionResult BusLocation(string id)
        {
            var busLocation = _busRepository.GetBusLocationById(id);
            if (busLocation == null)
                return Success(StatusCodes.Status200OK, message: "Failed to find the bus");

            return Success(StatusCodes.Status200OK, response: busLocation.ToBusLocationItemResponse());
        }
    }
}
