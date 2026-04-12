using NeoHub.Services.Models;

namespace NeoHub.Services
{
    public interface IPanelUserService
    {
        Task<PanelUserReadResult> ReadAllAsync(string sessionId, CancellationToken ct);
        Task<PanelUserWriteResult> WriteUserAsync(string sessionId, PanelUserState user, PanelUserState original, CancellationToken ct);
    }
}
