namespace Backend.Models
{
    public record BusCallingPointItemResponse: IResponse
    {
        public required int Sequence { get; init; }

        public required DateTime ScheduledTime { get; init; }

        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }

        public required string Name { get; init; }
    }
}
