namespace Backend.Models
{
    // One pair of stops a line runs between, and how the timetable names them. Held in memory only, so it carries the
    // timetable's fields directly and none of the times, since a pair is the same whichever departure produced it.
    public record BusRoute
    {
        public required string OperatorName { get; init; }

        public required string OriginBusStopId { get; init; }
        public required string OriginName { get; init; }

        public required string DestinationBusStopId { get; init; }
        public required string DestinationName { get; init; }

        public required string Direction { get; init; }
    }
}
