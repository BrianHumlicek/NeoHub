namespace NeoHub.Services.Diagnostics
{
    public record DiagnosticsLogEntry
    {
        public required DateTime Timestamp { get; init; }
        public required LogLevel LogLevel { get; init; }
        public required string Category { get; init; }
        public required string Message { get; init; }
        public Exception? Exception { get; init; }
        public string? SessionId { get; init; }
    }
}