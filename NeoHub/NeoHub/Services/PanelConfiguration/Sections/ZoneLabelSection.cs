using System.Text;
using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Zone labels from installer programming section [000][001].
/// Address: [000][001][zone] — 56 bytes per zone (28 UTF-16BE chars: two 14-char display lines).
/// </summary>
public class ZoneLabelSection(PanelCapabilities capabilities)
    : SectionGroup<string>(capabilities)
{
    private const int CharsPerLabel = 28;
    private const int BytesPerLabel = CharsPerLabel * 2; // UTF-16BE
    private const int BatchSize = 4;

    public override string DisplayName => "Zone Label";
    public override int MaxItems => Capabilities.MaxZones;

    protected override ushort[] GetItemAddress(int item) => [0, 1, (ushort)item];

    protected override string[] DeserializeAll(byte[] data, int count)
    {
        var result = new string[count];
        int labelSize = count > 0 && data.Length > 0 ? data.Length / count : BytesPerLabel;
        for (int i = 0; i < count && i * labelSize < data.Length; i++)
            result[i] = Encoding.BigEndianUnicode.GetString(data, i * labelSize, labelSize).TrimEnd();
        return result;
    }

    protected override byte[] SerializeAll(string[] values)
    {
        var data = new byte[values.Length * BytesPerLabel];
        for (int i = 0; i < values.Length; i++)
        {
            string padded = (values[i] ?? "").PadRight(CharsPerLabel)[..CharsPerLabel];
            Encoding.BigEndianUnicode.GetBytes(padded, 0, CharsPerLabel, data, i * BytesPerLabel);
        }
        return data;
    }

    public override async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        var values = new string[MaxItems];

        for (int start = 1; start <= MaxItems; start += BatchSize)
        {
            int count = Math.Min(BatchSize, MaxItems - start + 1);
            var response = await send(
                new SectionRead { SectionAddress = GetItemAddress(start), Count = (byte)count }, ct);

            if (response?.SectionData is not null)
            {
                int labelSize = response.SectionData.Length / count;
                for (int i = 0; i < count && i * labelSize < response.SectionData.Length; i++)
                    values[start - 1 + i] = Encoding.BigEndianUnicode
                        .GetString(response.SectionData, i * labelSize, labelSize).TrimEnd();
            }
        }

        Import(SerializeAll(values));
    }

    public override async Task<SectionResult> WriteAsync(
        SendSectionWrite send, int item, string value, CancellationToken ct)
    {
        string padded = (value ?? "").PadRight(CharsPerLabel)[..CharsPerLabel];
        byte[] data = Encoding.BigEndianUnicode.GetBytes(padded);
        var result = await send(
            new SectionWrite { SectionAddress = GetItemAddress(item), SectionData = data }, ct);

        if (result.Success)
            this[item] = value?.TrimEnd() ?? "";

        return result;
    }

    /// <summary>Snapshot of zones with labels (filters out null/empty), 1-indexed.</summary>
    public override IReadOnlyList<(int Number, string Value)> Items =>
        base.Items.Where(e => !string.IsNullOrEmpty(e.Value)).ToList();
}
