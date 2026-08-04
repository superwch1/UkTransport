namespace Backend.Models
{
    public record BusTimetablesResponse : IResponse
    {
        public required IReadOnlyList<IReadOnlyList<BusTimetableItemResponse>> BusTimetables { get; init; }
    }
}
