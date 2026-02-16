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
        public string? ErrorMessage { get; init; }

        public static PanelCommandResult Ok() => new() { Success = true };
        public static PanelCommandResult Error(string message) => new() { Success = false, ErrorMessage = message };
    }
}
