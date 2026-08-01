using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models
{
    [Index(nameof(BusTimetableId))]
    public record BusSpecialDay
    {
        [Key]
        public long Id { get; init; }

        [ForeignKey(nameof(BusTimetable))]
        public required string BusTimetableId { get; init; }
        public virtual BusTimetable? BusTimetable { get; init; }

        // Both ends are inclusive. A range with no end date covers its start day only.
        public required DateOnly StartDate { get; init; }
        public required DateOnly EndDate { get; init; }

        public required bool IsOperating { get; init; }
    }
}
