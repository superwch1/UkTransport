using Backend.Enumerations;

namespace Backend.Models
{
    public record BusTimetable
    {
        // Stable id, e.g. "{NationalOperatorCode}-{VehicleJourney @id}".
        public required string Id { get; init; }

        public required string OperatorRef { get; init; }
        public required string LineName { get; init; }          // e.g. "343"

        public required string OriginName { get; init; }
        public required string DestinationName { get; init; }
        public required Direction Direction { get; init; }

        // Operating period (your filename's 20260719_20310719).
        public required DateOnly ValidFrom { get; init; }
        public required DateOnly ValidTo { get; init; }

        // Days this journey runs.
        public required bool Monday { get; init; }
        public required bool Tuesday { get; init; }
        public required bool Wednesday { get; init; }
        public required bool Thursday { get; init; }
        public required bool Friday { get; init; }
        public required bool Saturday { get; init; }
        public required bool Sunday { get; init; }

        public required bool RunsOnBankHolidays { get; init; }

        public required IReadOnlyList<BusCallingPoint> BusCallingPoints { get; init; }
    }
}
