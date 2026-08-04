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


        [ForeignKey(nameof(BusTimetable))]
        public required string BusTimetableId { get; init; }
        public virtual BusTimetable? BusTimetable { get; init; }

        public required int Sequence { get; init; }
        public required string BusStopId { get; init; }

        public required string Name { get; init; }

        [Precision(9, 6)]
        public required decimal Latitude { get; init; }

        [Precision(9, 6)]
        public required decimal Longitude { get; init; }

        // Measured from midnight starting the journey's operating day, so a stop reached after midnight simply runs
        // past 24 hours: a bus calling at 01:10 the next morning is 25:10. Nothing wraps and no day offset is needed.
        public required TimeSpan ScheduledTime { get; init; }
    }
}