namespace Backend.Models
{
    public record BusJourneyResponse : IResponse
    {
        public required string JourneyKey { get; init; }
        public required string OperatorName { get; init; }
        public required string LineName { get; init; }
              

        public required string Direction { get; init; }

        public required string OriginName { get; init; }
        public required string OriginBusStopId { get; init; }
        public required TimeOnly OriginDepartureTime { get; init; }


        public required string DestinationName { get; init; }
        public required string DestinationBusStopId { get; init; }
        public required TimeOnly? DestinationArrivalTime { get; init; }


        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }


        // delay (+), arrive early (-)
        public required int ScheduleOffsetMinutes { get; init; }
        public required DateTime RecordedAtTime { get; init; }


        public required IReadOnlyList<BusCallingPointItemResponse> BusCallingPoints { get; init; }
    }
}
