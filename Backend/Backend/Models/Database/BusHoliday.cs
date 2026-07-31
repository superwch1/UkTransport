using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models
{
    [Index(nameof(BusTimetableId))]
    [Index(nameof(PublicHolidayName))]
    public record BusHoliday
    {
        [Key]
        public long Id { get; init; }

        [ForeignKey(nameof(BusTimetable))]
        public required string BusTimetableId { get; init; }
        public virtual BusTimetable? BusTimetable { get; init; }


        [ForeignKey(nameof(PublicHoliday))]
        public required string PublicHolidayName { get; init; }
        public virtual PublicHoliday? PublicHoliday { get; init; }


        public required bool IsOperating { get; init; }
    }
}
