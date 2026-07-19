namespace Backend.Models
{
    public record BusStop
    {
        public required string Id { get; init; }

        public required string CommonName { get; init; }

        public required int Bearing { get; init; }

        public required decimal Latitude { get; init; }

        public required decimal Longitude { get; init; }
    }
}
