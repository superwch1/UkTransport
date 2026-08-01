using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models
{
    [Index(nameof(BusStopId))]
    [Index(nameof(BusTimetableId))]
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

        public TimeOnly ScheduledTime { get; init; }
        public required int ScheduledDayOffset { get; init; } // Day offset relative to the journey's first operating day (0-based).
    }
}