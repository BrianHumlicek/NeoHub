using DSC.TLink.ITv2;

namespace NeoHub.Services.Settings;

/// <summary>
/// Query helpers for <see cref="PanelConnectionsSettings"/>. The repeated case-insensitive
/// <c>FirstOrDefault</c> pattern shows up in every page that knows about a session, so it's
/// factored into one place.
/// </summary>
public static class PanelConnectionsSettingsExtensions
{
    /// <summary>
    /// Returns the <see cref="ConnectionSettings"/> entry whose <see cref="ConnectionSettings.SessionId"/>
    /// matches <paramref name="sessionId"/> (case-insensitive), or null if no match exists or
    /// <paramref name="sessionId"/> is null/empty.
    /// </summary>
    public static ConnectionSettings? FindBySessionId(this PanelConnectionsSettings settings, string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || settings.Connections.Count == 0)
            return null;

        return settings.Connections.FirstOrDefault(c =>
            string.Equals(c.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
    }
}
