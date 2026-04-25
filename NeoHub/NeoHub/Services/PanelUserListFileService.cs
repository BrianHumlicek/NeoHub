using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DSC.TLink.ITv2.Enumerations;
using NeoHub.Services.Models;
using NeoHub.Services.Settings;

namespace NeoHub.Services;

/// <summary>
/// JSON envelope for a panel's user list. Capability fields are informational only —
/// imports populate whatever's in the file into the target <see cref="PanelUserListState"/>
/// and leave slots not in the file untouched (merge semantics).
/// </summary>
public record PanelUserListFile
{
    public string FormatVersion { get; init; } = "1.0";
    public DateTime ExportedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Informational: the panel's reported <c>MaxUsers</c> at export time.</summary>
    public int MaxUsers { get; init; }

    /// <summary>Informational: the panel's reported <c>MaxPartitions</c> at export time.</summary>
    public int MaxPartitions { get; init; }

    public List<PanelUserRecord> Users { get; init; } = new();
}

/// <summary>
/// Serialized form of a single <see cref="PanelUserState"/>. The access code is stored as a
/// plaintext string — exports contain real PINs. Treat the file as sensitive.
/// </summary>
public record PanelUserRecord
{
    public int UserIndex { get; init; }
    public string? Label { get; init; }
    public string? Code { get; init; }
    public int? CodeLength { get; init; }

    /// <summary>Attributes flags as a raw byte for lossless round-trip.</summary>
    public byte Attributes { get; init; }

    public List<byte> Partitions { get; init; } = new();
    public bool HasProximityTag { get; init; }
}

/// <summary>
/// Serialization, server-side persistence, and print-rendering for a
/// <see cref="PanelUserListState"/>. Mirrors <see cref="PanelConfiguration.IPanelConfigFileService"/>.
/// No diff/compare is provided — user lists are small enough that a full replace (or merge via
/// <see cref="ImportIntoAsync"/>) is always the right answer.
/// </summary>
public interface IPanelUserListFileService
{
    /// <summary>
    /// Serializes the user list to a JSON byte array. <paramref name="maxUsers"/> and
    /// <paramref name="maxPartitions"/> are stored in the file header as context for future readers
    /// and print output; they are not required on import.
    /// </summary>
    /// <remarks>Exports include plaintext access codes. The resulting file is sensitive.</remarks>
    byte[] ExportToBytes(PanelUserListState userList, int maxUsers, int maxPartitions);

    /// <summary>
    /// Merges the file's users into <paramref name="target"/>. Slots present in the file overwrite
    /// matching slots in the target; slots absent from the file are left alone. Returns the header
    /// info from the file so callers can compare against current capabilities if desired.
    /// </summary>
    PanelUserListFile ImportInto(byte[] data, PanelUserListState target);

    /// <summary>Saves the user list to a file in the server's persist folder.</summary>
    Task SaveToServerAsync(PanelUserListState userList, int maxUsers, int maxPartitions, string fileName);

    /// <summary>Reads a user list file from the server's persist folder without importing it.</summary>
    Task<byte[]> LoadBytesFromServerAsync(string fileName);

    /// <summary>Lists saved user list files on the server.</summary>
    IReadOnlyList<string> GetSavedFiles();

    /// <summary>
    /// Generates a printer-friendly HTML document. When <paramref name="revealCodes"/> is false,
    /// access codes are masked in the output — suitable for paper copies that might be misplaced.
    /// </summary>
    string GeneratePrintHtml(
        PanelUserListState userList, int maxUsers, int maxPartitions,
        bool revealCodes = false, string? sessionId = null);
}

public class PanelUserListFileService : IPanelUserListFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dir;
    private readonly ILogger<PanelUserListFileService> _logger;

    public PanelUserListFileService(ISettingsPersistenceService persistence, ILogger<PanelUserListFileService> logger)
    {
        _dir = Path.Combine(persistence.PersistPath, "users");
        Directory.CreateDirectory(_dir);
        _logger = logger;
    }

    // ── Serialization ────────────────────────────────────────────────────────

    public byte[] ExportToBytes(PanelUserListState userList, int maxUsers, int maxPartitions)
    {
        var file = new PanelUserListFile
        {
            ExportedAtUtc = userList.LastReadAt ?? DateTime.UtcNow,
            MaxUsers = maxUsers,
            MaxPartitions = maxPartitions,
            Users = userList.Users.Values
                .OrderBy(u => u.UserIndex)
                .Select(ToRecord)
                .ToList(),
        };

        return JsonSerializer.SerializeToUtf8Bytes(file, JsonOptions);
    }

    public PanelUserListFile ImportInto(byte[] data, PanelUserListState target)
    {
        var file = JsonSerializer.Deserialize<PanelUserListFile>(data, JsonOptions)
            ?? throw new InvalidOperationException("Invalid user list file");

        foreach (var record in file.Users)
        {
            target.Users[record.UserIndex] = FromRecord(record);
        }

        target.LastReadAt = file.ExportedAtUtc;
        _logger.LogInformation("Imported {Count} users from file (v{Version})", file.Users.Count, file.FormatVersion);
        return file;
    }

    private static PanelUserRecord ToRecord(PanelUserState u) => new()
    {
        UserIndex = u.UserIndex,
        Label = u.UserLabel,
        Code = u.CodeValue,
        CodeLength = u.CodeLength,
        Attributes = (byte)u.Attributes,
        Partitions = new List<byte>(u.Partitions),
        HasProximityTag = u.HasProximityTag,
    };

    private static PanelUserState FromRecord(PanelUserRecord r) => new()
    {
        UserIndex = r.UserIndex,
        UserLabel = r.Label,
        CodeValue = r.Code,
        CodeLength = r.CodeLength,
        Attributes = (PanelUserAttributes)r.Attributes,
        Partitions = new List<byte>(r.Partitions),
        HasProximityTag = r.HasProximityTag,
        LastUpdated = DateTime.UtcNow,
    };

    // ── Server persistence ───────────────────────────────────────────────────

    public async Task SaveToServerAsync(PanelUserListState userList, int maxUsers, int maxPartitions, string fileName)
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var path = Path.Combine(_dir, Path.GetFileName(fileName));
        var bytes = ExportToBytes(userList, maxUsers, maxPartitions);
        await File.WriteAllBytesAsync(path, bytes);
        _logger.LogInformation("Saved user list to {Path}", path);
    }

    public async Task<byte[]> LoadBytesFromServerAsync(string fileName)
    {
        var path = Path.Combine(_dir, Path.GetFileName(fileName));
        if (!File.Exists(path))
            throw new FileNotFoundException($"User list file not found: {fileName}");

        _logger.LogInformation("Loaded user list from {Path}", path);
        return await File.ReadAllBytesAsync(path);
    }

    public IReadOnlyList<string> GetSavedFiles()
    {
        if (!Directory.Exists(_dir))
            return [];

        return Directory.GetFiles(_dir, "*.json")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderByDescending(f => f)
            .ToList();
    }

    // ── Print ────────────────────────────────────────────────────────────────

    public string GeneratePrintHtml(
        PanelUserListState userList, int maxUsers, int maxPartitions,
        bool revealCodes = false, string? sessionId = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.AppendLine("<title>Panel User List</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; margin: 20px; }");
        sb.AppendLine("h1 { font-size: 16px; margin-bottom: 4px; }");
        sb.AppendLine(".meta { color: #666; font-size: 10px; margin-bottom: 16px; }");
        sb.AppendLine(".warn { color: #b26a00; font-size: 10px; margin-bottom: 12px; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-bottom: 12px; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 3px 6px; text-align: left; vertical-align: top; }");
        sb.AppendLine("th { background: #f5f5f5; font-weight: 600; }");
        sb.AppendLine("td.num { text-align: right; font-weight: 600; width: 40px; }");
        sb.AppendLine("td.code { font-family: 'Courier New', monospace; }");
        sb.AppendLine("td.status { width: 70px; }");
        sb.AppendLine("tr.group-header td { background: #eee; font-weight: 600; padding: 6px 8px; }");
        sb.AppendLine("tr.master td.status { color: #0366d6; font-weight: 600; }");
        sb.AppendLine("tr.disabled td.status { color: #b26a00; }");
        sb.AppendLine("@media print { body { margin: 0; } }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>Panel User List</h1>");
        sb.Append("<div class='meta'>");
        if (sessionId is not null)
            sb.Append($"Session: {Escape(sessionId)} &middot; ");
        sb.Append($"{maxUsers} user slots &middot; {maxPartitions} partitions");
        if (userList.LastReadAt is not null)
            sb.Append($" &middot; Read: {userList.LastReadAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.Append($" &middot; Printed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("</div>");

        if (revealCodes)
            sb.AppendLine("<div class='warn'>⚠ Access codes shown in plaintext. Handle this document accordingly.</div>");

        sb.AppendLine("<table><thead><tr>");
        sb.AppendLine("<th style='width:30px;'>#</th>");
        sb.AppendLine("<th style='width:70px;'>Status</th>");
        sb.AppendLine("<th>Label</th>");
        sb.AppendLine("<th style='width:90px;'>Code</th>");
        sb.AppendLine("<th style='width:70px;'>Type</th>");
        sb.AppendLine("<th>Attributes</th>");
        sb.AppendLine("<th>Partitions</th>");
        sb.AppendLine("</tr></thead><tbody>");

        // Two groups, mirroring the on-screen layout. Reads are all-or-nothing, so every entry
        // is guaranteed to be either Active or Disabled.
        var ordered = userList.Users.Values.OrderBy(u => u.UserIndex).ToList();
        var active = ordered.Where(u => u.IsActive).ToList();
        var disabled = ordered.Where(u => u.IsDisabled).ToList();

        AppendGroup(sb, "Active", active, revealCodes);
        AppendGroup(sb, "Disabled", disabled, revealCodes);

        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</body></html>");
        return sb.ToString();

        static void AppendGroup(StringBuilder sb, string label, IReadOnlyList<PanelUserState> users, bool revealCodes)
        {
            if (users.Count == 0) return;

            sb.AppendLine($"<tr class='group-header'><td colspan='7'>{label} ({users.Count})</td></tr>");

            foreach (var u in users)
            {
                var rowClass = u.IsMaster ? "master" : u.IsDisabled ? "disabled" : "";
                var status = u.IsMaster ? "Master" : u.IsDisabled ? "Disabled" : "Active";

                sb.Append($"<tr class='{rowClass}'>");
                sb.Append($"<td class='num'>{u.UserIndex}</td>");
                sb.Append($"<td class='status'>{status}</td>");
                sb.Append($"<td>{Escape(u.DisplayLabel)}</td>");
                sb.Append($"<td class='code'>{FormatCode(u, revealCodes)}</td>");
                sb.Append($"<td>{FormatType(u)}</td>");
                sb.Append($"<td>{FormatAttributes(u.Attributes)}</td>");
                sb.Append($"<td>{FormatPartitions(u.Partitions)}</td>");
                sb.AppendLine("</tr>");
            }
        }

        static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);

        static string FormatCode(PanelUserState u, bool reveal)
        {
            if (u.IsDisabled || string.IsNullOrEmpty(u.CodeValue)) return "—";
            return reveal ? Escape(u.CodeValue) : new string('*', Math.Max(4, u.CodeValue.Length));
        }

        static string FormatType(PanelUserState u)
        {
            if (!u.IsActive) return "—";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(u.CodeValue)) parts.Add("PIN");
            if (u.HasProximityTag) parts.Add("Prox");
            return parts.Count > 0 ? string.Join(" + ", parts) : "—";
        }

        static string FormatAttributes(PanelUserAttributes attrs)
        {
            if (attrs == 0) return "—";
            var names = new List<string>();
            if (attrs.HasFlag(PanelUserAttributes.Supervisor)) names.Add("Supervisor");
            if (attrs.HasFlag(PanelUserAttributes.DuressCode)) names.Add("Duress");
            if (attrs.HasFlag(PanelUserAttributes.CanBypassZone)) names.Add("Bypass");
            if (attrs.HasFlag(PanelUserAttributes.RemoteAccess)) names.Add("Remote");
            if (attrs.HasFlag(PanelUserAttributes.BellSquawk)) names.Add("Squawk");
            if (attrs.HasFlag(PanelUserAttributes.OneTimeUse)) names.Add("One-Time");
            return string.Join(", ", names);
        }

        static string FormatPartitions(List<byte> parts)
            => parts.Count == 0 ? "—" : string.Join(", ", parts.OrderBy(p => p).Select(p => $"P{p}"));
    }
}
