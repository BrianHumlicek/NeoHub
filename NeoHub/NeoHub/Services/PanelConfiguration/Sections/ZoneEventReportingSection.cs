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
