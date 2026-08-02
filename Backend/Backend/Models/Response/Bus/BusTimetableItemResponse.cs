namespace Backend.Models
{
    public record BusTimetableItemResponse : IResponse
    {
        public required string JourneyKey { get; init; }
        public required string RouteKey { get; init; }

        public required string Direction { get; init; }
        public required IReadOnlyList<BusCallingPointItemResponse> CallingPoints { get; init; }
    }
}
