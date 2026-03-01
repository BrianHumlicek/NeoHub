using DSC.TLink;
using DSC.TLink.ITv2.Enumerations;

namespace NeoHub.Services
{
    /// <summary>
    /// Service for sending commands to the alarm panel.
    /// Abstracts the TLink session layer from the UI and API.
    /// </summary>
    public interface IPanelCommandService
    {
        Task<PanelCommandResult> ArmAsync(string sessionId, byte partition, ArmingMode mode, string? accessCode = null);
        Task<PanelCommandResult> DisarmAsync(string sessionId, byte partition, string? accessCode = null);
    }

    public record PanelCommandResult
    {
        public bool Success { get; init; }

        /// <summary>Infrastructure error code. Null when the panel itself rejected the command.</summary>
        public TLinkErrorCode? ErrorCode { get; init; }

        /// <summary>Human-readable error detail. Set for both infrastructure and panel-level failures.</summary>
        public string? ErrorMessage { get; init; }

        public static PanelCommandResult Ok() => new() { Success = true };

        public static PanelCommandResult Error(TLinkErrorCode code, string message) =>
            new() { Success = false, ErrorCode = code, ErrorMessage = message };

        public static PanelCommandResult Error(string message) =>
            new() { Success = false, ErrorMessage = message };
    }
}
