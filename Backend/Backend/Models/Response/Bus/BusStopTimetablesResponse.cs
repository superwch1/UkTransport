namespace Backend.Models
{
    public record BusStopTimetablesResponse : IResponse
    {
        public required IReadOnlyList<BusStopTimetableItemResponse> BusStopTimetables { get; init; }
    }
}
