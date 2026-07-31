namespace Backend.Models
{
    public record BusJourney
    {
        public required string Id { get; init; }

        public required string DatasetId { get; init; }

        public required string OperatorId { get; init; }
        public required string OperatorName { get; init; }

        public required string LineName { get; init; }

        public required string OriginName { get; init; }
        public required string DestinationName { get; init; }

        // possible value: inbound, outbound, clockwise, anticlockwise, 1, 2
        public required string DirectionRef { get; init; }

        // sometimes the bus departure from 22:00 and arrive at 01:10 (that will be a day offset)
        public required int ScheduledDayOffset { get; init; }

        // {originDepartureTime}-{originBusStopId}-{destinationBusStopId}
        public required string TripScheduleKey { get; init; }


        public required string OriginBusStopId { get; init; }
        public required TimeOnly OriginAimedDepartureTime { get; init; }

        public required string DestinationBusStopId { get; init; }
        public required TimeOnly? DestinationAimedArrivalTime { get; init; }


        public required IReadOnlyList<BusCallingPoint>? BusCallingPoints { get; init; }


        // calling point the bus was last seen at, or null while it has not been seen at one. So the calling points cannot jump form 5 to 40 in a round trip
        public required int? LastSeenSequence { get; init; }

        // delay (+), arrive early (-)
        public required int ScheduleOffsetMinutes { get; init; }


        // Where the bus was when it last reported.
        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }

        // When the live feed recorded the position this was worked out from, so the journey ages by what the bus
        // reported rather than by when it happened to be processed.
        public required DateTime RecordedAtTime { get; init; }
    }
}
