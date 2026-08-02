namespace Backend.Models
{
    public record BusRoute
    {
        // {lineName}-{originBusStopId}-{destinationBusStopId}
        public required string RouteKey { get; init; }

        public required string LineName { get; init; }
        public required string OperatorName { get; init; }

        public required string OriginBusStopId { get; init; }
        public required string OriginName { get; init; }

        public required string DestinationBusStopId { get; init; }
        public required string DestinationName { get; init; }

        public required string Direction { get; init; }
        public required TimeSpan Duration { get; init; }
    }
}
