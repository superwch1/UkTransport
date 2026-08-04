namespace Backend.Models
{
    public record BusRouteItemResponse: IResponse
    {
        public required string RouteKey { get; init; }
        public required string LineName { get; init; }
        public required string OperatorName { get; init; }

        public required string OriginBusStopId { get; init; }
        public required string OriginName { get; init; }

        public required string DestinationBusStopId { get; init; }
        public required string DestinationName { get; init; }

        public required string Direction { get; init; }
    }
}
