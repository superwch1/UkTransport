namespace Backend.Models
{
    public record BusStopTimetableItemResponse : IResponse
    {
        public required string LineName { get; init; }

        public required TimeOnly ScheduledTime { get; init; }
    }
}
