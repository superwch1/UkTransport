namespace Backend.Models
{
    public record BusJourney
    {
        // {lineName}-{originDepartureTime}-{originBusStopId}-{destinationBusStopId}
        public required string JourneyKey { get; init; }

        public required string DatasetId { get; init; }

        public required string OperatorId { get; init; }
        public required string OperatorName { get; init; }

        public required string LineName { get; init; }

        public required string OriginName { get; init; }
        public required string DestinationName { get; init; }

        // possible value: inbound, outbound, clockwise, anticlockwise, 1, 2
        public required string Direction { get; init; }

        // sometimes the bus departure from 22:00 and arrive at 01:10 (that will be a day offset)
        public required int ScheduledDayOffset { get; init; }


        public required string OriginBusStopId { get; init; }
        public required TimeOnly OriginDepartureTime { get; init; }

        public required string DestinationBusStopId { get; init; }
        public required TimeOnly DestinationArrivalTime { get; init; }


        public required IReadOnlyList<BusCallingPoint> BusCallingPoints { get; init; }


        // calling point the bus was last arrived at, or null while it has not been seen at one. So the calling points cannot jump form 5 to 40 in a round trip
        public required int? LastArrivedStopSequence { get; init; }

        // delay (+), arrive early (-)
        public required int ScheduleOffsetMinutes { get; init; }


        // Where the bus was when it last reported.
        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
        public required decimal Bearing { get; init; }

        public required DateTime RecordedAtTime { get; init; }
    }
}
