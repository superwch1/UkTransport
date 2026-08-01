using Backend.Enumerations;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models
{
    [Index(nameof(LineName))]
    [Index(nameof(OperatorId))]
    [Index(nameof(JourneyKey))]
    [Index(nameof(RouteKey))]
    [Index(nameof(DepartureTime))]
    [Index(nameof(OriginBusStopId))]
    [Index(nameof(DestinationBusStopId))]
    public record BusTimetable
    {
        [Key]
        public required string Id { get; init; }


        public required string JourneyKey { get; init; } // {lineName}-{originDepartureTime}-{originBusStopId}-{destinationBusStopId}
        public required string RouteKey { get; init; } // {lineName}-{originBusStopId}-{destinationBusStopId}


        [ForeignKey(nameof(BusDataset))]
        public required string DatasetId { get; init; }
        public virtual BusDataset? BusDataset { get; init; }


        public required string OperatorId { get; init; }
        public required string OperatorName { get; init; }
        public required string LineName { get; init; }


        public required TimeOnly DepartureTime { get; init; }
        public required string OriginBusStopId { get; init; }
        public required string DestinationBusStopId { get; init; }
        public required string Direction { get; init; }


        // sometimes the bus departure from 22:00 and arrive at 01:10 (that will be a day offset)
        // then query take consideration to yesterday with day offset value 1
        public required int ScheduledDayOffset { get; init; }


        public required DateOnly StartDate { get; init; }
        public required DateOnly EndDate { get; init; }


        // don't change to flag since it is faster per column read instead of reading bit flags
        public required bool Monday { get; init; }
        public required bool Tuesday { get; init; }
        public required bool Wednesday { get; init; }
        public required bool Thursday { get; init; }
        public required bool Friday { get; init; }
        public required bool Saturday { get; init; }
        public required bool Sunday { get; init; }

        // Narrows the days above to certain weeks of the month. None for almost every journey.
        public required WeekOfMonth WeeksOfMonth { get; init; }

        public required IReadOnlyList<BusCallingPoint>? BusCallingPoints { get; init; }
        public required IReadOnlyList<BusSpecialDay>? BusSpecialDays { get; init; }
        public required IReadOnlyList<BusHoliday>? BusHolidays { get; init; }
    }

    public static class BusTimeTableExtension
    {
        public static string BuildJourneyKey(string lineName, TimeOnly departureTime, string originBusStopId, string destinationBusStopId)
        {
            return $"{lineName}-{departureTime.ToString("HH:mm")}-{originBusStopId}-{destinationBusStopId}";
        }

        public static string BuildRouteKey(string lineName, string originBusStopId, string destinationBusStopId)
        {
            return $"{lineName}-{originBusStopId}-{destinationBusStopId}";
        }

        // The source is part of the key because nothing stops two services numbering a dataset the same way.
        public static string BuildDatasetKey(string source, string sourceId)
        {
            return $"{source}:{sourceId}";
        }
    }
}
