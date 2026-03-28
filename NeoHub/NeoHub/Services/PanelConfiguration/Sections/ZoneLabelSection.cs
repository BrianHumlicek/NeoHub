using System.Text;
using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Zone labels from installer programming section [000][001].
/// Address: [000][001][zone] — 56 bytes per zone (28 UTF-16BE chars: two 14-char display lines).
/// </summary>
public class ZoneLabelSection
{
    private readonly PanelCapabilities _capabilities;
    private string[] _values = [];

    /// <summary>
    /// Raw label strings (28 chars each, trimmed). First 14 chars = line 1, last 14 = line 2.
    /// </summary>
    public IReadOnlyList<string> Values => _values;

    /// <summary>Snapshot of zones with labels (filters out null/empty), 1-indexed.</summary>
    public IReadOnlyList<(int Number, string Value)> Items
    {
        get
        {
            var values = _values;
            return values
                .Select((v, i) => (Number: i + 1, Value: v))
                .Where(e => !string.IsNullOrEmpty(e.Value))
                .ToList();
        }
    }

    public ZoneLabelSection(PanelCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

    public async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        _values = new string[_capabilities.MaxZones];
        const int batchSize = 4;

        for (int start = 1; start <= _capabilities.MaxZones; start += batchSize)
        {
            int count = Math.Min(batchSize, _capabilities.MaxZones - start + 1);
            var response = await send(new SectionRead { SectionAddress = [0, 1, (ushort)start], Count = (byte)count }, ct);
            if (response?.SectionData is not null)
            {
                int labelSize = response.SectionData.Length / count;
                for (int i = 0; i < count && i * labelSize < response.SectionData.Length; i++)
                    _values[start - 1 + i] = Encoding.BigEndianUnicode.GetString(response.SectionData, i * labelSize, labelSize).TrimEnd();
            }
        }
    }

    public async Task ReadAsync(SendSectionRead send, int zone, CancellationToken ct)
    {
        var response = await send(new SectionRead { SectionAddress = [0, 1, (ushort)zone] }, ct);
        if (response?.SectionData is { Length: >= 2 })
            _values[zone - 1] = Encoding.BigEndianUnicode.GetString(response.SectionData).TrimEnd();
    }
    public async Task<SectionResult> WriteAsync(SendSectionWrite send, int zone, string label, CancellationToken ct)
    {
        string paddedLabel = label.PadRight(28)[..28];
        byte[] data = Encoding.BigEndianUnicode.GetBytes(paddedLabel);
        var result = await send(new SectionWrite { SectionAddress = [0, 1, (ushort)zone], SectionData = data }, ct);
        if (result.Success)
            _values[zone - 1] = label.TrimEnd();
        return result;
    }
}
