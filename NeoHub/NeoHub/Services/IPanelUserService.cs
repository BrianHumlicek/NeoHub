namespace NeoHub.Services
{
    public interface IPanelUserService
    {
        Task<PanelUserReadResult> ReadAllAsync(string sessionId, CancellationToken ct);
    }
}
