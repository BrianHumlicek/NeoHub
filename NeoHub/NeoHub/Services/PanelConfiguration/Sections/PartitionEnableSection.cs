namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Partition enable/disable from installer programming section [200].
/// Address: [200][partition] — one byte per partition.
/// </summary>
public class PartitionEnableSection(PanelCapabilities capabilities)
    : SectionGroup<PartitionEnable>(capabilities)
{
    public override string DisplayName => "Partition Enable";
    public override int MaxItems => Capabilities.MaxPartitions;

    protected override ushort[] GetItemAddress(int item) => [200, (ushort)item];

    protected override PartitionEnable[] DeserializeAll(byte[] data, int count)
    {
        var result = new PartitionEnable[count];
        for (int i = 0; i < Math.Min(data.Length, count); i++)
            result[i] = (PartitionEnable)data[i];
        return result;
    }

    protected override byte[] SerializeAll(PartitionEnable[] values)
        => values.Select(v => (byte)v).ToArray();

    public override string FormatItemValue(int item)
    {
        var value = this[item];
        var raw = (byte)value;
        var bits = Convert.ToString(raw, 2).PadLeft(8, '0');
        return $"{raw:X2} [{bits[..4]} {bits[4..]}] {value}";
    }
}

public enum PartitionEnable : byte
{
    Disabled = 0,
    Enabled = 1,
}
