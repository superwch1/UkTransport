namespace Backend.Models
{
    public record BusTimetablesResponse : IResponse
    {
        public required IReadOnlyList<BusTimetableItemResponse> BusTimetables { get; init; }
    }
}
