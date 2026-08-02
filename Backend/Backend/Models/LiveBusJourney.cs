namespace Backend.Models
{
    public record LiveBusJourney
    {
        // {lineName}-{originDepartureTime}-{originBusStopId}-{destinationBusStopId}
        public required string JourneyKey { get; init; }
        public required string RouteKey { get; init; }


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
