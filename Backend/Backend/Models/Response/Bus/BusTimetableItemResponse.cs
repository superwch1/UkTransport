namespace Backend.Models
{
    public record BusTimetableItemResponse : IResponse
    {
        public required string JourneyKey { get; init; }
        public required DateTime ScheduledDepartureTime { get; init; }
        public required IReadOnlyList<BusCallingPointItemResponse> CallingPoints { get; init; }
    }
}
