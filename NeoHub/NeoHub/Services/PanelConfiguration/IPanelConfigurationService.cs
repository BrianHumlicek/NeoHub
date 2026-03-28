using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration;

/// <summary>
/// Constrained delegate for sending a SectionRead to the panel.
/// Section classes receive this as their only interaction with the session layer —
/// they cannot enter/exit config mode or send arbitrary commands.
/// </summary>
public delegate Task<SectionReadResponse?> SendSectionRead(SectionRead request, CancellationToken ct);

/// <summary>Result of a configuration read attempt.</summary>
public record ConfigReadResult(bool Success, string? ErrorMessage = null);

public interface IPanelConfigurationService
{
    /// <summary>
    /// Enter installer config mode, read all known sections, exit config mode.
    /// Results are stored on SessionState.Configuration.
    /// </summary>
    Task<ConfigReadResult> ReadAllAsync(string sessionId, string installerCode, CancellationToken ct);
}
