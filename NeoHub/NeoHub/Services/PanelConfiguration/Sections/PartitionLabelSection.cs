using System.Text;
using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Partition labels from installer programming section [000].
/// Address: [000][100 + partition] — 56 bytes per partition (28 UTF-16BE chars).
/// </summary>
public class PartitionLabelSection
{
    private readonly PanelCapabilities _capabilities;
    private string[] _values = [];

    public IReadOnlyList<string> Values => _values;

    /// <summary>Snapshot of partitions with labels (filters out null/empty), 1-indexed.</summary>
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

    public PartitionLabelSection(PanelCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

    public async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        _values = new string[_capabilities.MaxPartitions];
        const int batchSize = 4;

        for (int start = 1; start <= _capabilities.MaxPartitions; start += batchSize)
        {
            int count = Math.Min(batchSize, _capabilities.MaxPartitions - start + 1);
            var response = await send(new SectionRead { SectionAddress = [0, (ushort)(100 + start)], Count = (byte)count }, ct);
            if (response?.SectionData is not null)
            {
                int labelSize = response.SectionData.Length / count;
                for (int i = 0; i < count && i * labelSize < response.SectionData.Length; i++)
                    _values[start - 1 + i] = Encoding.BigEndianUnicode.GetString(response.SectionData, i * labelSize, labelSize).TrimEnd();
            }
        }
    }

    public async Task ReadAsync(SendSectionRead send, int partition, CancellationToken ct)
    {
        var response = await send(new SectionRead { SectionAddress = [0, (ushort)(100 + partition)] }, ct);
        if (response?.SectionData is { Length: >= 2 })
            _values[partition - 1] = Encoding.BigEndianUnicode.GetString(response.SectionData).TrimEnd();
    }
}
