using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public record PublicHoliday
    {
        // The holiday name as TransXChange gave it, normalised to upper case letters and digits.
        [Key]
        public required string Name { get; init; }
    }
}
