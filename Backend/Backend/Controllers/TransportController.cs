using Backend.Extensions;
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

        [HttpGet("[controller]/[action]")]
        public IActionResult BusLocations([FromQuery] decimal north, [FromQuery] decimal south, [FromQuery] decimal east, [FromQuery] decimal west)
        {
            var busLocations = _busRepository.GetBusLocations(north, south, east, west);
            if (busLocations.Count > 100)
                return Success(StatusCodes.Status204NoContent, message: "Zoom in to show bus real time location");

            return Success(StatusCodes.Status204NoContent, response: busLocations.ToBusLocationsResponse());
        }
    }
}
