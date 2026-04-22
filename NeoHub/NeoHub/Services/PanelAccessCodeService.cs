using Microsoft.Extensions.Options;
using NeoHub.Services.Settings;
using DSC.TLink.ITv2;

namespace NeoHub.Services;

/// <summary>
/// Identifies which access code on a panel-connection setting to update.
/// Maps to the corresponding string property on <see cref="ConnectionSettings"/>.
/// </summary>
public enum PanelAccessCodeKind
{
    Installer,
    Master,
}

/// <summary>
/// Keeps the persisted access codes in <see cref="PanelConnectionsSettings"/> in sync with values
/// that have just been written to the panel. Pages that change a panel access code should call the
/// matching method here so future operations (for the same session) authenticate with the new value
/// instead of a stale cached one.
/// </summary>
/// <remarks>
/// Page-local caches (e.g., the <c>_masterCode</c> field on <c>PanelUsers.razor</c>) remain the
/// caller's responsibility — this service only owns the persisted setting.
/// </remarks>
public interface IPanelAccessCodeService
{
    /// <summary>
    /// Updates and persists the matching access code on the connection settings entry whose
    /// <c>SessionId</c> matches <paramref name="sessionId"/> (case-insensitive). No-ops if no
    /// matching connection exists or the value is already current.
    /// </summary>
    /// <returns>
    /// True when the setting was changed and persisted; false when no matching connection exists
    /// or the value was already current.
    /// </returns>
    Task<bool> UpdateConnectionCodeAsync(string sessionId, PanelAccessCodeKind kind, string? newCode);
}

public class PanelAccessCodeService(
    IOptionsMonitor<PanelConnectionsSettings> connectionSettings,
    ISettingsPersistenceService persistence,
    ILogger<PanelAccessCodeService> logger)
    : IPanelAccessCodeService
{
    public async Task<bool> UpdateConnectionCodeAsync(string sessionId, PanelAccessCodeKind kind, string? newCode)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;

        var conn = connectionSettings.CurrentValue.Connections
            .FirstOrDefault(c => string.Equals(c.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (conn is null) return false;

        var normalized = string.IsNullOrEmpty(newCode) ? null : newCode;

        var current = kind switch
        {
            PanelAccessCodeKind.Installer => conn.InstallerCode,
            PanelAccessCodeKind.Master => conn.MasterCode,
            _ => null,
        };
        if (current == normalized) return false;

        switch (kind)
        {
            case PanelAccessCodeKind.Installer: conn.InstallerCode = normalized; break;
            case PanelAccessCodeKind.Master: conn.MasterCode = normalized; break;
        }

        await persistence.SaveSettingsAsync(typeof(PanelConnectionsSettings), connectionSettings.CurrentValue);
        logger.LogInformation(
            "Updated {Kind} code in connection settings for session {SessionId}",
            kind, sessionId);
        return true;
    }
}
