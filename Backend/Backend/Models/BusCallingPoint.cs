using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models
{
    [Index(nameof(BusStopId))]
    public record BusCallingPoint
    {
        [Key]
        public long Id { get; init; }

        // Foreign key back to the parent journey — this is the shared key.
        [ForeignKey(nameof(BusTimetable))]
        public required string BusTimetableId { get; init; }
        public virtual BusTimetable? BusTimetable { get; init; }

        public required int Sequence { get; init; }     
        public required string BusStopId { get; init; }

        // allow to group by line name and operator ref to find timetable of a bus stop for each line
        public required string LineName { get; init; }
        public required string OperatorRef { get; init; }

        // A stop can have both, origin stop only have departure, destination stop only have arrival and rest have both
        public TimeOnly? ArrivalTime { get; init; }
        public TimeOnly? DepartureTime { get; init; }

        public int? ArrivalDayOffset { get; init; } // Day offset relative to the journey's first operating day (0-based).
    }
}