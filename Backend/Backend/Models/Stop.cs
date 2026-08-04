using Backend.Enumerations;

namespace Backend.Models
{
    public record Stop
    {
        public required string Id { get; init; }

        public required int Bearing { get; init; }
        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }

        public required StopType StopType { get; init; }
    }
}
