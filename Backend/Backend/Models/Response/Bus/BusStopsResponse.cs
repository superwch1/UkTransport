namespace Backend.Models
{
    public record BusStopsResponse : IResponse
    {
        public required IReadOnlyList<BusStopItemResponse> BusStops { get; init; }
    }
}
