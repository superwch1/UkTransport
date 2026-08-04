using Backend.Enumerations;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Models
{
    [Index(nameof(JourneyKey))]
    [Index(nameof(RouteKey))]
    [Index(nameof(DepartureTime))]
    [Index(nameof(ArrivalTime))]
    public record BusTimetable
    {
        [Key]
        public required string Id { get; init; }


        public required string JourneyKey { get; init; } // {lineName}-{originDepartureTime}-{originBusStopId}-{destinationBusStopId}
        public required string RouteKey { get; init; } // {lineName}-{originBusStopId}-{destinationBusStopId}
        public required string StopPatternKey { get; init; } // fingerprint of the calling points


        [ForeignKey(nameof(BusDataset))]
        public required string DatasetId { get; init; }
        public virtual BusDataset? BusDataset { get; init; }


        public required string OperatorId { get; init; }
        public required string OperatorName { get; init; }
        public required string LineName { get; init; }


        public required TimeSpan DepartureTime { get; init; }
        public required string OriginBusStopId { get; init; }
        public required string OriginName { get; init; }


        public required TimeSpan ArrivalTime { get; init; }
        public required string DestinationBusStopId { get; init; }
        public required string DestinationName { get; init; }

        public required string Direction { get; init; }


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


        // Most journeys touch a single region; a cross-border one such as Chester to Wrexham sets two.
        public required bool NorthEast { get; init; }
        public required bool NorthWest { get; init; }
        public required bool YorkshireAndTheHumber { get; init; }
        public required bool EastMidlands { get; init; }
        public required bool WestMidlands { get; init; }
        public required bool EastOfEngland   { get; init; }
        public required bool London { get; init; }
        public required bool SouthEast { get; init; }
        public required bool SouthWest { get; init; }
        public required bool Wales { get; init; }
        public required bool Scotland { get; init; }
        public required bool NorthernIreland { get; init; }

        // How many of the four UK countries the journey runs through. The nine English regions count as one between
        // them, so this is not the number of regions above: a journey crossing London and the South East stays at 1,
        // while one running Chester to Wrexham is 2.
        public required int CountryCount { get; init; }

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

        public static string BuildStopPatternKey(IReadOnlyList<string> busStopIds)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('-', busStopIds)));
            return Convert.ToHexString(hash, 0, 8);
        }

        // The source is part of the key because nothing stops two services numbering a dataset the same way.
        public static string BuildDatasetKey(string source, string sourceId)
        {
            return $"{source}:{sourceId}";
        }
    }
}
