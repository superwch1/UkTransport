using Backend.Models;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    public abstract class ApiControllerBase: ControllerBase
    {
        protected IActionResult Success(int statusCode, IResponse? response = null, string message = "")
        {
            return StatusCode(
                statusCode,
                new ApiResponse()
                {
                    Data = response,
                    Message = message,
                    TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier,
                    ResponseTime = DateTimeOffset.UtcNow,
                }
            );
        }

        protected IActionResult Failure(int statusCode, string message)
        {
            return StatusCode(
                statusCode,
                new ApiResponse()
                {
                    Data = null,
                    Message = message,
                    TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier,
                    ResponseTime = DateTimeOffset.UtcNow,
                }
            );
        }
    }
}
