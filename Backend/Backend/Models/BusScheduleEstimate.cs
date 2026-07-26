namespace Backend.Models
{
    public record BusScheduleEstimate
    {
        public required int Sequence { get; init; }

        // delay (+), arrive early (-)
        public required int ScheduleOffsetMinutes { get; init; }

        public required DateTimeOffset CalculatedAt { get; init; }
    }
}
