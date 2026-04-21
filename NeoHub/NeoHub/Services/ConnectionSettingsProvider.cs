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
    private readonly ISessionMonitor _sessionMonitor;
    private readonly ILogger<ConnectionSettingsProvider> _logger;

    public ConnectionSettingsProvider(
        IOptionsMonitor<PanelConnectionsSettings> settings,
        ISettingsPersistenceService persistence,
        ISessionMonitor sessionMonitor,
        ILogger<ConnectionSettingsProvider> logger)
    {
        _settings = settings;
        _persistence = persistence;
        _sessionMonitor = sessionMonitor;
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
            // Fall through — try factory defaults on the newly created placeholder
            existing = connections.Last();
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

        if (existing.IsComplete)
        {
            _logger.LogInformation("Resolved connection settings for session {SessionId}", sessionId);
            return existing;
        }

        // Incomplete — try a trial copy with factory defaults
        if (existing.HasEncryptionKey(encryptionType))
        {
            // Has the right key but something else is incomplete — can't help with defaults
            _logger.LogWarning(
                "Connection settings for {SessionId} are incomplete. Please configure encryption keys.",
                sessionId);
            return null;
        }

        _logger.LogInformation(
            "Connection settings for {SessionId} are incomplete. Trying factory default encryption key.",
            sessionId);

        return new ConnectionSettings
        {
            SessionId = existing.SessionId,
            EncryptionType = encryptionType,
            IntegrationAccessCodeType1 = existing.IntegrationAccessCodeType1
                ?? (encryptionType == EncryptionType.Type1 ? ConnectionSettings.FactoryDefaultType1 : null),
            IntegrationAccessCodeType2 = existing.IntegrationAccessCodeType2
                ?? (encryptionType == EncryptionType.Type2 ? ConnectionSettings.FactoryDefaultType2 : null),
            DefaultAccessCode = existing.DefaultAccessCode,
            InstallerCode = existing.InstallerCode,
            MasterCode = existing.MasterCode,
            MaxZones = existing.MaxZones
        };
    }

    public void ConfirmDefaults(string sessionId)
    {
        var existing = _settings.CurrentValue.Connections.FirstOrDefault(c =>
            string.Equals(c.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        if (existing == null || existing.IsComplete)
            return;

        // The handshake succeeded with factory defaults — persist them
        if (string.IsNullOrWhiteSpace(existing.IntegrationAccessCodeType1)
            && existing.EncryptionType == EncryptionType.Type1)
        {
            existing.IntegrationAccessCodeType1 = ConnectionSettings.FactoryDefaultType1;
        }

        if (string.IsNullOrWhiteSpace(existing.IntegrationAccessCodeType2)
            && existing.EncryptionType == EncryptionType.Type2)
        {
            existing.IntegrationAccessCodeType2 = ConnectionSettings.FactoryDefaultType2;
        }

        _logger.LogInformation(
            "Factory defaults confirmed for {SessionId} — persisting to settings", sessionId);
        PersistSettingsAsync();
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
        _sessionMonitor.NotifyChanged();
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
