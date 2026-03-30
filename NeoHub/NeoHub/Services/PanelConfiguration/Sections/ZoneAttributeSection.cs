namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Zone attribute flags from installer programming section [002].
/// Address: [002][zone] — two bytes per zone (big-endian ushort).
/// </summary>
public class ZoneAttributeSection(PanelCapabilities capabilities)
    : SectionGroup<ZoneAttributes>(capabilities)
{
    public override string DisplayName => "Zone Attributes";
    public override int MaxItems => Capabilities.MaxZones;

    protected override ushort[] GetItemAddress(int item) => [2, (ushort)item];

    protected override ZoneAttributes[] DeserializeAll(byte[] data, int count)
    {
        var result = new ZoneAttributes[count];
        int elementCount = Math.Min(data.Length / 2, count);
        for (int i = 0; i < elementCount; i++)
            result[i] = new ZoneAttributes(
                (ZoneFunctionalAttributes)data[i * 2],
                (ZonePhysicalAttributes)data[i * 2 + 1]);
        return result;
    }

    protected override byte[] SerializeAll(ZoneAttributes[] values)
    {
        var data = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            data[i * 2] = (byte)values[i].Functional;
            data[i * 2 + 1] = (byte)values[i].Physical;
        }
        return data;
    }
}

public readonly record struct ZoneAttributes(ZoneFunctionalAttributes Functional, ZonePhysicalAttributes Physical);

[Flags]
public enum ZoneFunctionalAttributes : byte
{
    IsAudible = 0x01,
    IsPulsedOrSteady = 0x02,
    IsDoorChime = 0x04,
    IsBypassable = 0x08,
    IsForceArmable = 0x10,
    IsSwingerShutdown = 0x20,
    IsTransmissionDelay = 0x40,
    IsBurglaryVerified = 0x80,
}
[Flags]
public enum ZonePhysicalAttributes : byte
{
    IsNormallyClosedLoop = 0x01,
    IsSingleEOLResistor = 0x02,
    IsDoubleEOLResistor = 0x04,
    IsFastLoopResponse = 0x08,
    IsTwoWayAudio = 0x10,
    IsHoldupVerified = 0x20,
}
