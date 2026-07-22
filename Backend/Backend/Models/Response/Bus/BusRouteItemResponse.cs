namespace Backend.Models
{
    public record BusRouteItemResponse: IResponse
    {
        public required int Sequence { get; init; }
        public required string BusStopId { get; init; }

        public TimeOnly ScheduledTime { get; init; }

        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
    }
}
