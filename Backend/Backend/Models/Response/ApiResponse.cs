namespace Backend.Models
{
    public class ApiResponse
    {
        public required string Message { get; init; }

        public required object? Data { get; init; }

        public required string TraceId { get; init; }

        public required DateTimeOffset ResponseTime { get; init; }
    }
}
