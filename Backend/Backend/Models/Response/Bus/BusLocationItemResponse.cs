namespace Backend.Models
{
    public record BusLocationItemResponse : IResponse
    {
        public required string Id { get; init; }
        public required DateTime RecordedAtTime { get; init; }

        public required string OperatorRef { get; init; }
        public required string PublishedLineName { get; init; }

        public required string OriginName { get; init; }
        public required string OriginRef { get; init; }
        public required TimeOnly? OriginAimedDepartureTime { get; init; }


        public required string DestinationName { get; init; }
        public required string DestinationRef { get; init; }
        public required TimeOnly? DestinationAimedArrivalTime { get; init; }

        public required string VehicleRef { get; init; }

        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }
    }
}
