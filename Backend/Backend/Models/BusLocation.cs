namespace Backend.Models
{
    public record BusLocation
    {
        public required string TripScheduleKey { get; init; }
        public required DateTime RecordedAtTime { get; init; }


        // Operator and service metadata
        public required string OperatorRef { get; init; }
        public required string LineName { get; init; }
        public required string Direction { get; init; }


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
