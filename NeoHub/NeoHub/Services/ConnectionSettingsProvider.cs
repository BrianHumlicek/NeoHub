using DSC.TLink.ITv2;
using DSC.TLink.ITv2.Enumerations;
using Microsoft.Extensions.Options;
using NeoHub.Services.Settings;

namespace NeoHub.Services;

/// <summary>
/// Resolves per-connection settings from <see cref="PanelConnectionsSettings.Connections"/>.
/// Creates placeholder entries for unknown panels and persists them to userSettings.json.
/// </summary>
public class ConnectionSettingsProvider : IConnectionSettingsProvider
{
    private readonly IOptionsMonitor<PanelConnectionsSettings> _settings;
    private readonly ISettingsPersistenceService _persistence;
    private readonly ILogger<ConnectionSettingsProvider> _logger;

    public ConnectionSettingsProvider(
        IOptionsMonitor<PanelConnectionsSettings> settings,
        ISettingsPersistenceService persistence,
        ILogger<ConnectionSettingsProvider> logger)
    {
        _settings = settings;
        _persistence = persistence;
        _logger = logger;
    }

    public ConnectionSettings? ResolveConnection(string sessionId, EncryptionType encryptionType)
    {
        var connections = _settings.CurrentValue.Connections;
        var existing = connections.FirstOrDefault(c =>
            string.Equals(c.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            _logger.LogWarning(
                "Unknown panel {SessionId} (encryption: {EncryptionType}). Creating placeholder connection entry.",
                sessionId, encryptionType);
            CreatePlaceholder(sessionId, encryptionType);
            return null;
        }

        // Always update encryption type to match what the panel actually uses
        if (encryptionType != EncryptionType.Unknown && existing.EncryptionType != encryptionType)
        {
            _logger.LogInformation(
                "Updating encryption type for {SessionId} from {Old} to {New}",
                sessionId, existing.EncryptionType, encryptionType);
            existing.EncryptionType = encryptionType;
            PersistSettingsAsync();
        }

        if (!existing.IsComplete)
        {
            _logger.LogWarning(
                "Connection settings for {SessionId} are incomplete. Please configure encryption keys.",
                sessionId);
            return null;
        }

        _logger.LogInformation("Resolved connection settings for session {SessionId}", sessionId);
        return existing;
    }

    private void CreatePlaceholder(string sessionId, EncryptionType encryptionType)
    {
        var placeholder = new ConnectionSettings
        {
            SessionId = sessionId,
            EncryptionType = encryptionType
        };

        _settings.CurrentValue.Connections.Add(placeholder);
        PersistSettingsAsync();
    }

    private async void PersistSettingsAsync()
    {
        try
        {
            await _persistence.SaveSettingsAsync(typeof(PanelConnectionsSettings), _settings.CurrentValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist connection settings");
        }
    }
}
