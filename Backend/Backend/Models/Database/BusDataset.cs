using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public record BusDataset
    {
        // "{Source}:{SourceId}", built by BusTimeTableExtension.BuildDatasetKey, since not every source numbers its
        // datasets the way BODS does.
        [Key]
        public required string Id { get; init; }

        public required DateTimeOffset ImportedAt { get; init; }
    }
}
