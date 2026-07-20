namespace Backend.Models
{
    public record BusLocationsResponse : IResponse
    {
        public required IReadOnlyList<BusLocationItemResponse> BusLocations { get; init; }
    }
}
