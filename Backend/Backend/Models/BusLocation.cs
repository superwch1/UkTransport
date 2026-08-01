namespace Backend.Models
{
    public record BusLocation
    {
        public required string JourneyKey { get; init; }
        public required DateTime RecordedAtTime { get; init; }

        public required string LineName { get; init; }


        // Origin and destination
        public required string OriginBusStopId { get; init; }
        public required TimeOnly OriginAimedDepartureTime { get; init; }

        public required string DestinationBusStopId { get; init; }
        public required TimeOnly? DestinationAimedArrivalTime { get; init; }


        // Real-time location
        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }
    }
}
