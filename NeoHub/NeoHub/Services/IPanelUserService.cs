using NeoHub.Services.Models;

namespace NeoHub.Services
{
    public interface IPanelUserService
    {
        Task<PanelUserReadResult> ReadAllAsync(string sessionId, string masterCode, CancellationToken ct);
        Task<PanelUserWriteResult> WriteUserAsync(string sessionId, PanelUserState user, PanelUserState original, string masterCode, CancellationToken ct);

        /// <summary>
        /// Quickly tests whether the panel accepts <paramref name="masterCode"/> by entering
        /// and immediately exiting AccessCodeProgramming mode. Returns true on success.
        /// Used by the operator prompt dialog to validate the code before any real read/write.
        /// </summary>
        Task<bool> VerifyMasterCodeAsync(string sessionId, string masterCode, CancellationToken ct);

        /// <summary>
        /// Disables a user slot by writing the all-'A' sentinel to the access code.
        /// The panel zeros attributes and partition assignments as a side-effect; this call
        /// re-reads the slot afterward so the returned state matches the panel exactly.
        /// </summary>
        Task<PanelUserWriteResult> DisableUserAsync(string sessionId, int userIndex, string masterCode, CancellationToken ct);

        /// <summary>
        /// Enables a previously-disabled user slot by writing a new numeric access code.
        /// The panel applies its own default attributes and partition assignments; this call
        /// re-reads the slot afterward so the returned state matches the panel exactly.
        /// </summary>
        Task<PanelUserWriteResult> EnableUserAsync(string sessionId, int userIndex, string newCode, string masterCode, CancellationToken ct);
    }
}
