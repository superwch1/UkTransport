namespace Backend.Models
{
    public record BusScheduleEstimate
    {
        public required int Sequence { get; init; }

        // delay (+), arrive early (-)
        public required int ScheduleOffsetMinutes { get; init; }

        // When the live feed recorded the position this was worked out from.
        public required DateTime RecordedAtTime { get; init; }
    }
}
