namespace Backend.Models
{
    public record BusRoutesResponse : IResponse
    {
        public required IReadOnlyList<BusRouteItemResponse> BusRoutes { get; init; }
    }
}
