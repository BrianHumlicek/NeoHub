using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Zone event reporting flags from installer programming section [307].
/// Address: [307][zone] — one byte per zone.
/// </summary>
public class ZoneEventReportingSection(PanelCapabilities capabilities)
    : SectionGroup<ZoneEventReportingAttributes>(capabilities)
{
    public override string DisplayName => "Zone Event Reporting";
    public override int MaxItems => Capabilities.MaxZones;

    protected override ushort[] GetItemAddress(int item) => [307, (ushort)item];

    protected override ZoneEventReportingAttributes[] DeserializeAll(byte[] data, int count)
    {
        var result = new ZoneEventReportingAttributes[count];
        for (int i = 0; i < Math.Min(data.Length, count); i++)
            result[i] = (ZoneEventReportingAttributes)data[i];
        return result;
    }

    protected override byte[] SerializeAll(ZoneEventReportingAttributes[] values)
        => values.Select(v => (byte)v).ToArray();

    public override string FormatItemValue(int item)
    {
        var value = this[item];
        var raw = (byte)value;
        if (raw == 0)
            return "";

        var bits = Convert.ToString(raw, 2).PadLeft(8, '0');
        var names = Enum.GetValues<ZoneEventReportingAttributes>()
            .Where(f => (byte)f != 0
                     && ((byte)f & ((byte)f - 1)) == 0
                     && value.HasFlag(f))
            .Select(f => f.ToString());
        return $"{raw:X2} [{bits[..4]} {bits[4..]}] ({string.Join(", ", names)})";
    }
}

[Flags]
public enum ZoneEventReportingAttributes : byte
{
    Alarm = 0x01,
    AlarmRestore = 0x02,
    Tamper = 0x04,
    TamperRestore = 0x08,
    Fault = 0x10,
    FaultRestore = 0x20
}
