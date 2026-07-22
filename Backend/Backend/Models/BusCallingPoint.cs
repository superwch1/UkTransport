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

        // A stop can have both; intermediate stops often only one.
        public TimeOnly? ArrivalTime { get; init; }
        public TimeOnly? DepartureTime { get; init; }

        public int? ArrivalDayOffset { get; init; } // Day offset relative to the journey's first operating day (0-based).
        public int? DepartureDayOffset { get; init; }
    }
}