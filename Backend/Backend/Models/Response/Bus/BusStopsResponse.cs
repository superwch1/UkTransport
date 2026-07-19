namespace Backend.Models
{
    public class BusStopsResponse : IResponse
    {
        public required IReadOnlyList<BusStopItemResponse> BusStops { get; init; }
    }
}
