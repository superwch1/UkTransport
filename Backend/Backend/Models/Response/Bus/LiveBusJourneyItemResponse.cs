namespace Backend.Models
{
    public record LiveBusJourneyItemResponse : IResponse
    {
        public required string JourneyKey { get; init; }


        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }


        // delay (+), arrive early (-)
        public required int ScheduleOffsetMinutes { get; init; }
        public required DateTime RecordedAtTime { get; init; }
    }
}
