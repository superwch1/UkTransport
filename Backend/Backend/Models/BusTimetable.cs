using Backend.Enumerations;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    [Index(nameof(LineName))]
    [Index(nameof(OperatorRef))]
    [Index(nameof(OriginDepartureKey))]
    public record BusTimetable
    {
        [Key]
        public required string Id { get; init; }

        public required string OperatorRef { get; init; }
        public required string LineName { get; init; }          // e.g. "343"

        public required string OriginName { get; init; }
        public required string DestinationName { get; init; }
        public required Direction Direction { get; init; }


        // sometimes the bus departure from 22:00 and arrive at 01:10 (that will be a day offset)
        // then query take consideration to yesterday with day offset value 1
        public required int ArrivalDayOffset { get; init; }


        // Operating period (your filename's 20260719_20310719).
        public required DateOnly ValidFrom { get; init; }
        public required DateOnly ValidTo { get; init; }

        // no need line name since there can be changed
        // {originDepartureTime}-{originBusStopId}-{destinationBusStopId}
        public required string OriginDepartureKey { get; init; }


        // Days this journey runs. (don't change to flag since it is faster per column read instead of reading bit flags)
        public required bool Monday { get; init; }
        public required bool Tuesday { get; init; }
        public required bool Wednesday { get; init; }
        public required bool Thursday { get; init; }
        public required bool Friday { get; init; }
        public required bool Saturday { get; init; }
        public required bool Sunday { get; init; }

        public required bool RunsOnBankHolidays { get; init; }

        public required IReadOnlyList<BusCallingPoint>? BusCallingPoints { get; init; }
    }

    public static class BusTimeTableExtension
    {
        public static string CreateOriginDepartureKey(TimeOnly departureTime, string originBusStopId, string destinationBusStopId)
        {
            return $"{departureTime.ToString("HH:mm")}-{originBusStopId}-{destinationBusStopId}";
        }
    }
}
