using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoHub.Services.Settings;

/// <summary>
/// One-time migration from the legacy flat "DSC.TLink" settings format to the new
/// "PanelConnections" + "Application.ListenPort" structure.
///
/// Runs at startup before configuration is bound. Safe to remove once all
/// deployments have been migrated.
///
/// Old format:
///   "DSC.TLink": {
///     "IntegrationAccessCodeType1": "...",
///     "IntegrationAccessCodeType2": "...",
///     "IntegrationIdentificationNumber": "...",
///     "ListenPort": 3072,
///     "MaxZones": 7
///   }
///
/// New format:
///   "PanelConnections": {
///     "Connections": [ { "SessionId": "...", ... } ]
///   }
///   "Application": {
///     "ListenPort": 3072
///   }
/// </summary>
public static class SettingsMigration
{
    private const string LegacySectionName = "DSC.TLink";

    /// <summary>
    /// Migrates the settings file in-place if the legacy "DSC.TLink" section is detected.
    /// Should be called before <c>AddJsonFile</c> in Program.cs.
    /// </summary>
    public static void MigrateIfNeeded(string contentRootPath)
    {
        var filePath = SettingsPersistenceService.GetSettingsFilePath(contentRootPath);

        if (!File.Exists(filePath))
            return;

        try
        {
            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var root = JsonNode.Parse(json);
            if (root is not JsonObject rootObj)
                return;

            if (!rootObj.ContainsKey(LegacySectionName))
                return;

            var legacy = rootObj[LegacySectionName]?.AsObject();
            if (legacy == null)
                return;

            Migrate(rootObj, legacy);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(filePath, rootObj.ToJsonString(options));

            Console.WriteLine($"[SettingsMigration] Migrated legacy \"{LegacySectionName}\" section in {filePath}");
        }
        catch (Exception ex)
        {
            // Migration failure should not prevent startup — the app can still
            // run with defaults or manual config.
            Console.WriteLine($"[SettingsMigration] Warning: migration failed — {ex.Message}");
        }
    }

    private static void Migrate(JsonObject root, JsonObject legacy)
    {
        // Build the connection entry from legacy fields
        var connection = new JsonObject();

        MoveProperty(legacy, "IntegrationIdentificationNumber", connection, "SessionId");
        MoveProperty(legacy, "IntegrationAccessCodeType1", connection, "IntegrationAccessCodeType1");
        MoveProperty(legacy, "IntegrationAccessCodeType2", connection, "IntegrationAccessCodeType2");
        MoveProperty(legacy, "MaxZones", connection, "MaxZones");

        // Only create a connection entry if there was a session ID
        var sessionId = connection["SessionId"]?.GetValue<string>();
        var hasConnection = !string.IsNullOrWhiteSpace(sessionId);

        // Create PanelConnections section
        if (!root.ContainsKey(PanelConnectionsSettings.SectionName))
        {
            var connections = new JsonArray();
            if (hasConnection)
                connections.Add(connection);

            root[PanelConnectionsSettings.SectionName] = new JsonObject
            {
                ["Connections"] = connections
            };
        }

        // Move ListenPort to Application section
        if (legacy.ContainsKey("ListenPort"))
        {
            var listenPort = legacy["ListenPort"]?.DeepClone();
            var appSection = root[ApplicationSettings.SectionName]?.AsObject();
            if (appSection == null)
            {
                appSection = new JsonObject();
                root[ApplicationSettings.SectionName] = appSection;
            }

            if (!appSection.ContainsKey("ListenPort"))
                appSection["ListenPort"] = listenPort;
        }

        // Remove legacy section
        root.Remove(LegacySectionName);
    }

    private static void MoveProperty(JsonObject source, string sourceKey, JsonObject target, string targetKey)
    {
        if (!source.ContainsKey(sourceKey))
            return;

        target[targetKey] = source[sourceKey]?.DeepClone();
    }
}
