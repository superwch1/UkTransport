namespace Backend.Models
{
    public record BusLocationItemResponse : IResponse
    {
        public required string JourneyKey { get; init; }
        public required DateTime RecordedAtTime { get; init; }

        public required string OperatorName { get; init; }
        public required string PublishedLineName { get; init; }

        public required string OriginName { get; init; }
        public required string OriginRef { get; init; }
        public required TimeOnly? OriginAimedDepartureTime { get; init; }


        public required string DestinationName { get; init; }
        public required string DestinationRef { get; init; }
        public required TimeOnly? DestinationAimedArrivalTime { get; init; }

        // delay (+), arrive early (-)
        public required int EstimatedScheduleOffset { get; init; }


        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }
    }
}
