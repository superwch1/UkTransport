namespace Backend.Models
{
    public record BusLocation
    {
        public required string OriginDepartureKey { get; init; }
        public required DateTime RecordedAtTime { get; init; }


        // Operator and service metadata
        public required string OperatorRef { get; init; }
        public required string PublishedLineName { get; init; }


        // Origin and destination
        public required string OriginName { get; init; }
        public required string OriginRef { get; init; }
        public required TimeOnly? OriginAimedDepartureTime { get; init; }


        public required string DestinationName { get; init; }
        public required string DestinationRef { get; init; }
        public required TimeOnly? DestinationAimedArrivalTime { get; init; }


        // Real-time location
        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }
    }
}
