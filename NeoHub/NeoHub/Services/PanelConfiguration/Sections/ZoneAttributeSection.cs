using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Zone attribute flags from installer programming section [002].
/// Address: [002][zone] — two bytes per zone (big-endian ushort).
/// </summary>
public class ZoneAttributeSection
{
    private readonly PanelCapabilities _capabilities;
    private ZoneAttributes[] _values = [];

    public IReadOnlyList<ZoneAttributes> Values => _values;

    /// <summary>Snapshot of zones with attributes set (filters out zero), 1-indexed.</summary>
    public IReadOnlyList<(int Number, ZoneAttributes Value)> Items
    {
        get
        {
            var values = _values;
            return values
                .Select((v, i) => (Number: i + 1, Value: v))
                .ToList();
        }
    }

    public ZoneAttributeSection(PanelCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

    public async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        _values = new ZoneAttributes[_capabilities.MaxZones];

        var response = await send(new SectionRead { SectionAddress = [2, 1], Count = (byte)_capabilities.MaxZones }, ct);
        if (response?.SectionData is not null)
        {
            int elementCount = Math.Min(response.SectionData.Length / 2, _values.Length);
            for (int i = 0; i < elementCount; i++)
                _values[i] = new ZoneAttributes((ZoneFunctionalAttributes)response.SectionData[i * 2], (ZonePhysicalAttributes)response.SectionData[i * 2 + 1]);
        }
    }

    public async Task ReadAsync(SendSectionRead send, int zone, CancellationToken ct)
    {
        var response = await send(new SectionRead { SectionAddress = [2, (ushort)zone] }, ct);
        if (response?.SectionData is { Length: >= 2 })
            _values[zone - 1] = new ZoneAttributes((ZoneFunctionalAttributes)response.SectionData[0], (ZonePhysicalAttributes)response.SectionData[1]);
    }

    public async Task<SectionResult> WriteAsync(SendSectionWrite send, int zone, ZoneAttributes attributes, CancellationToken ct)
    {        
        var result = await send(new SectionWrite { SectionAddress = [2, (ushort)zone], SectionData = [(byte)attributes.Functional, (byte)attributes.Physical] }, ct);
        if (result.Success)
            _values[zone - 1] = attributes;
        return result;
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
