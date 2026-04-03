namespace NeoHub.Services
{
    public record AccessCodeReadResult(bool Success, string? ErrorMessage = null)
    {
        public int ReadCount { get; init; }
        public int FailedCount { get; init; }
    }
}
