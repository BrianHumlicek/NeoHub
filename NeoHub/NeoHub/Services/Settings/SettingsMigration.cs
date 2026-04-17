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

            var dirty = false;

            // ── Migration 1: Legacy "DSC.TLink" flat format → PanelConnections + Application ──
            if (rootObj.ContainsKey(LegacySectionName))
            {
                var legacy = rootObj[LegacySectionName]?.AsObject();
                if (legacy != null)
                {
                    MigrateLegacySection(rootObj, legacy);
                    dirty = true;
                }
            }

            // ── Migration 2: Move DefaultAccessCode / DefaultInstallerCode from Application → each connection ──
            if (MigrateAccessCodesToConnections(rootObj))
                dirty = true;

            // ── Migration 3: Rename DefaultInstallerCode → InstallerCode in connection entries ──
            if (RenameConnectionCodeProperties(rootObj))
                dirty = true;

            if (dirty)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(filePath, rootObj.ToJsonString(options));
                Console.WriteLine($"[SettingsMigration] Migrated settings in {filePath}");
            }
        }
        catch (Exception ex)
        {
            // Migration failure should not prevent startup — the app can still
            // run with defaults or manual config.
            Console.WriteLine($"[SettingsMigration] Warning: migration failed — {ex.Message}");
        }
    }

    private static void MigrateLegacySection(JsonObject root, JsonObject legacy)
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

    /// <summary>
    /// Moves DefaultAccessCode and DefaultInstallerCode from the Application section
    /// into each existing connection entry using the new property names.
    /// Returns true if the file was modified.
    /// </summary>
    private static bool MigrateAccessCodesToConnections(JsonObject root)
    {
        var appSection = root[ApplicationSettings.SectionName]?.AsObject();
        if (appSection is null)
            return false;

        var hasAccessCode = appSection.ContainsKey("DefaultAccessCode");
        var hasInstallerCode = appSection.ContainsKey("DefaultInstallerCode");

        if (!hasAccessCode && !hasInstallerCode)
            return false;

        var accessCode = hasAccessCode ? appSection["DefaultAccessCode"]?.DeepClone() : null;
        var installerCode = hasInstallerCode ? appSection["DefaultInstallerCode"]?.DeepClone() : null;

        // Apply to each existing connection using the new property names
        var connections = root[PanelConnectionsSettings.SectionName]?["Connections"]?.AsArray();
        if (connections is not null)
        {
            foreach (var connNode in connections)
            {
                if (connNode is not JsonObject conn)
                    continue;

                if (accessCode is not null && !conn.ContainsKey("DefaultAccessCode"))
                    conn["DefaultAccessCode"] = accessCode.DeepClone();

                if (installerCode is not null && !conn.ContainsKey("InstallerCode"))
                    conn["InstallerCode"] = installerCode.DeepClone();
            }
        }

        // Remove from Application section
        if (hasAccessCode)
            appSection.Remove("DefaultAccessCode");
        if (hasInstallerCode)
            appSection.Remove("DefaultInstallerCode");

        return true;
    }

    /// <summary>
    /// Renames DefaultInstallerCode → InstallerCode in existing connection entries
    /// (for users who already ran migration 2 with the old name).
    /// Returns true if the file was modified.
    /// </summary>
    private static bool RenameConnectionCodeProperties(JsonObject root)
    {
        var connections = root[PanelConnectionsSettings.SectionName]?["Connections"]?.AsArray();
        if (connections is null)
            return false;

        var dirty = false;
        foreach (var connNode in connections)
        {
            if (connNode is not JsonObject conn)
                continue;

            if (conn.ContainsKey("DefaultInstallerCode") && !conn.ContainsKey("InstallerCode"))
            {
                conn["InstallerCode"] = conn["DefaultInstallerCode"]?.DeepClone();
                conn.Remove("DefaultInstallerCode");
                dirty = true;
            }
        }

        return dirty;
    }

    private static void MoveProperty(JsonObject source, string sourceKey, JsonObject target, string targetKey)
    {
        if (!source.ContainsKey(sourceKey))
            return;

        target[targetKey] = source[sourceKey]?.DeepClone();
    }
}
