namespace Backend.Models
{
    public record LiveBusJourneysResponse : IResponse
    {
        public required IReadOnlyList<LiveBusJourneyItemResponse> LiveBusJourneys { get; init; }
    }
}
