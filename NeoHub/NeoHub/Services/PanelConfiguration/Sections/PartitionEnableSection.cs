using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Partition enable/disable from installer programming section [200].
/// Address: [200][partition] — one byte per partition.
/// </summary>
public class PartitionEnableSection
{
    private readonly PanelCapabilities _capabilities;
    private PartitionEnable[] _values = [];

    public IReadOnlyList<PartitionEnable> Values => _values;

    /// <summary>Snapshot of all partitions with enable status, 1-indexed.</summary>
    public IReadOnlyList<(int Number, PartitionEnable Value)> Items
    {
        get
        {
            var values = _values;
            return values
                .Select((v, i) => (Number: i + 1, Value: v))
                .ToList();
        }
    }

    public PartitionEnableSection(PanelCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

    public async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        _values = new PartitionEnable[_capabilities.MaxPartitions];

        var response = await send(new SectionRead { SectionAddress = [200, 1], Count = (byte)_capabilities.MaxPartitions }, ct);
        if (response?.SectionData is not null)
        {
            for (int i = 0; i < Math.Min(response.SectionData.Length, _values.Length); i++)
                _values[i] = (PartitionEnable)response.SectionData[i];
        }
    }

    public async Task ReadAsync(SendSectionRead send, int partition, CancellationToken ct)
    {
        var response = await send(new SectionRead { SectionAddress = [200, (ushort)partition] }, ct);
        if (response?.SectionData is { Length: >= 1 })
            _values[partition - 1] = (PartitionEnable)response.SectionData[0];
    }
}

public enum PartitionEnable : byte
{
    Disabled = 0,
    Enabled = 1,
}
