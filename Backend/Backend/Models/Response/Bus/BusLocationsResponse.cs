namespace Backend.Models
{
    public class BusLocationsResponse : IResponse
    {
        public required IReadOnlyList<BusLocationItemResponse> BusLocations { get; init; }
    }
}
