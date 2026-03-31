using System.Text.Json;
using System.Text.Json.Serialization;
using NeoHub.Services.PanelConfiguration.Sections;
using NeoHub.Services.Settings;

namespace NeoHub.Services.PanelConfiguration;

/// <summary>
/// JSON-serializable envelope for a complete panel configuration snapshot.
/// Each section stores raw bytes for lossless round-tripping plus a human-readable
/// item map for inspection and manual editing.
/// </summary>
public record PanelConfigFile
{
    public string FormatVersion { get; init; } = "2.0";
    public DateTime ExportedAtUtc { get; init; } = DateTime.UtcNow;
    public PanelCapabilities Capabilities { get; init; } = null!;
    public Dictionary<string, PanelConfigSectionFile> Sections { get; init; } = new();
}

/// <summary>
/// A single section's data in the config file.
/// <see cref="Data"/> is the authoritative Base64 blob used for import.
/// <see cref="Items"/> is human-readable (item# → value) for inspection.
/// </summary>
public record PanelConfigSectionFile
{
    /// <summary>Base64-encoded raw section bytes — used for lossless import.</summary>
    public string Data { get; init; } = "";

    /// <summary>Human-readable item values keyed by 1-indexed item number.</summary>
    public Dictionary<string, string>? Items { get; init; }
}

/// <summary>A single item-level difference between two configuration states.</summary>
public record ConfigDiffEntry(
    string SectionName,
    int ItemNumber,
    string Address,
    string CurrentValue,
    string NewValue);

/// <summary>
/// Handles serialization of <see cref="PanelConfigurationState"/> to/from
/// a portable JSON file, and persistence to/from the server's settings folder.
/// </summary>
public interface IPanelConfigFileService
{
    /// <summary>Serializes a <see cref="PanelConfigurationState"/> to a JSON byte array.</summary>
    byte[] ExportToBytes(PanelConfigurationState config);

    /// <summary>Deserializes a JSON byte array into a <see cref="PanelConfigurationState"/>.</summary>
    PanelConfigurationState ImportFromBytes(byte[] data);

    /// <summary>Saves the configuration to a file in the server's persist folder.</summary>
    Task SaveToServerAsync(PanelConfigurationState config, string fileName);

    /// <summary>Loads a configuration from a file in the server's persist folder.</summary>
    Task<PanelConfigurationState> LoadFromServerAsync(string fileName);

    /// <summary>Lists saved configuration files on the server.</summary>
    IReadOnlyList<string> GetSavedFiles();

    /// <summary>Compares two configs and returns item-level differences.</summary>
    IReadOnlyList<ConfigDiffEntry> Compare(PanelConfigurationState current, PanelConfigurationState incoming);

    /// <summary>Generates a printer-friendly HTML document for a configuration.</summary>
    string GeneratePrintHtml(PanelConfigurationState config, string? sessionId = null);
}

public class PanelConfigFileService : IPanelConfigFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _configDir;
    private readonly ILogger<PanelConfigFileService> _logger;

    public PanelConfigFileService(ISettingsPersistenceService persistence, ILogger<PanelConfigFileService> logger)
    {
        _configDir = Path.Combine(persistence.PersistPath, "configs");
        Directory.CreateDirectory(_configDir);
        _logger = logger;
    }

    public byte[] ExportToBytes(PanelConfigurationState config)
    {
        var file = new PanelConfigFile
        {
            Capabilities = config.Capabilities,
            ExportedAtUtc = config.LastReadAt ?? DateTime.UtcNow,
        };

        foreach (var section in config.AllSections)
        {
            if (!section.IsSupported) continue;

            var items = new Dictionary<string, string>();
            for (int i = 1; i <= section.MaxItems; i++)
            {
                var value = section.FormatItemValue(i);
                if (!string.IsNullOrEmpty(value))
                    items[i.ToString()] = value;
            }

            file.Sections[section.DisplayName] = new PanelConfigSectionFile
            {
                Data = Convert.ToBase64String(section.Export()),
                Items = items.Count > 0 ? items : null,
            };
        }

        return JsonSerializer.SerializeToUtf8Bytes(file, JsonOptions);
    }

    public PanelConfigurationState ImportFromBytes(byte[] data)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;

        var capabilities = JsonSerializer.Deserialize<PanelCapabilities>(
            root.GetProperty("capabilities").GetRawText(), JsonOptions)
            ?? throw new InvalidOperationException("Invalid capabilities in config file");

        var config = new PanelConfigurationState(capabilities);

        if (root.TryGetProperty("exportedAtUtc", out var exportedAt))
            config.LastReadAt = exportedAt.GetDateTime();

        if (!root.TryGetProperty("sections", out var sectionsElement))
            return config;

        foreach (var section in config.AllSections)
        {
            if (!sectionsElement.TryGetProperty(
                    JsonNamingPolicy.CamelCase.ConvertName(section.DisplayName) is var camel
                        ? section.DisplayName : section.DisplayName,
                    out var sectionValue))
            {
                // Try exact match (covers both camelCase and PascalCase keys)
                bool found = false;
                foreach (var prop in sectionsElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, section.DisplayName, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionValue = prop.Value;
                        found = true;
                        break;
                    }
                }
                if (!found) continue;
            }

            string? base64 = null;

            if (sectionValue.ValueKind == JsonValueKind.String)
            {
                // v1 format: section value is a plain Base64 string
                base64 = sectionValue.GetString();
            }
            else if (sectionValue.ValueKind == JsonValueKind.Object
                     && sectionValue.TryGetProperty("data", out var dataElement))
            {
                // v2 format: section value is an object with a "data" field
                base64 = dataElement.GetString();
            }

            if (!string.IsNullOrEmpty(base64))
            {
                section.Import(Convert.FromBase64String(base64));
                _logger.LogDebug("Imported section {Section}", section.DisplayName);
            }
        }

        return config;
    }

    public async Task SaveToServerAsync(PanelConfigurationState config, string fileName)
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var path = Path.Combine(_configDir, Path.GetFileName(fileName));
        var bytes = ExportToBytes(config);
        await File.WriteAllBytesAsync(path, bytes);
        _logger.LogInformation("Saved panel config to {Path}", path);
    }

    public async Task<PanelConfigurationState> LoadFromServerAsync(string fileName)
    {
        var path = Path.Combine(_configDir, Path.GetFileName(fileName));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {fileName}");

        var bytes = await File.ReadAllBytesAsync(path);
        _logger.LogInformation("Loaded panel config from {Path}", path);
        return ImportFromBytes(bytes);
    }

    public IReadOnlyList<string> GetSavedFiles()
    {
        if (!Directory.Exists(_configDir))
            return [];

        return Directory.GetFiles(_configDir, "*.json")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderByDescending(f => f)
            .ToList();
    }

    public IReadOnlyList<ConfigDiffEntry> Compare(
        PanelConfigurationState current, PanelConfigurationState incoming)
    {
        var diffs = new List<ConfigDiffEntry>();

        for (int s = 0; s < current.AllSections.Count && s < incoming.AllSections.Count; s++)
        {
            var curSection = current.AllSections[s];
            var newSection = incoming.AllSections[s];

            if (!curSection.IsSupported || curSection.DisplayName != newSection.DisplayName)
                continue;

            // Quick whole-section check — skip item iteration if bytes are identical
            if (curSection.Export().AsSpan().SequenceEqual(newSection.Export()))
                continue;

            int maxItems = Math.Min(curSection.MaxItems, newSection.MaxItems);
            for (int i = 1; i <= maxItems; i++)
            {
                var curBytes = curSection.ExportItem(i);
                var newBytes = newSection.ExportItem(i);

                if (!curBytes.AsSpan().SequenceEqual(newBytes))
                {
                    diffs.Add(new ConfigDiffEntry(
                        curSection.DisplayName,
                        i,
                        curSection.FormatAddress(i),
                        curSection.FormatItemValue(i),
                        newSection.FormatItemValue(i)));
                }
            }
        }

        return diffs;
    }

    public string GeneratePrintHtml(PanelConfigurationState config, string? sessionId = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.AppendLine("<title>Panel Configuration</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; margin: 20px; }");
        sb.AppendLine("h1 { font-size: 16px; margin-bottom: 4px; }");
        sb.AppendLine(".meta { color: #666; font-size: 10px; margin-bottom: 16px; }");
        sb.AppendLine("h2 { font-size: 13px; margin-top: 16px; margin-bottom: 4px; border-bottom: 1px solid #ccc; padding-bottom: 2px; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-bottom: 12px; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 3px 6px; text-align: left; }");
        sb.AppendLine("th { background: #f5f5f5; font-weight: 600; }");
        sb.AppendLine("td.addr { font-family: 'Courier New', monospace; font-size: 10px; white-space: nowrap; }");
        sb.AppendLine("td.val { font-family: 'Courier New', monospace; }");
        sb.AppendLine("tr.empty td { color: #bbb; }");
        sb.AppendLine("@media print { body { margin: 0; } }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>Panel Configuration</h1>");
        sb.Append("<div class='meta'>");
        if (sessionId is not null)
            sb.Append($"Session: {Escape(sessionId)} &middot; ");
        sb.Append($"{config.Capabilities.MaxZones} zones &middot; {config.Capabilities.MaxPartitions} partitions");
        if (config.LastReadAt is not null)
            sb.Append($" &middot; Read: {config.LastReadAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.Append($" &middot; Printed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("</div>");

        foreach (var section in config.AllSections)
        {
            if (!section.IsSupported) continue;

            sb.AppendLine($"<h2>{Escape(section.DisplayName)}</h2>");
            sb.AppendLine("<table><thead><tr><th style='width:30px;'>#</th><th style='width:90px;'>Address</th><th>Value</th></tr></thead><tbody>");

            for (int i = 1; i <= section.MaxItems; i++)
            {
                var value = section.FormatItemValue(i);
                var isEmpty = string.IsNullOrEmpty(value);
                var rowClass = isEmpty ? " class='empty'" : "";

                sb.Append($"<tr{rowClass}>");
                sb.Append($"<td>{i}</td>");
                sb.Append($"<td class='addr'>{Escape(section.FormatAddress(i))}</td>");
                sb.Append($"<td class='val'>{Escape(isEmpty ? "—" : value)}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();

        static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);
    }
}
