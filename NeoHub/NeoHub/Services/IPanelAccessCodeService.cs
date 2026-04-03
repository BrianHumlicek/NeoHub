namespace NeoHub.Services
{
    public interface IPanelAccessCodeService
    {
        Task<AccessCodeReadResult> ReadAllAsync(string sessionId, CancellationToken ct);
    }
}
