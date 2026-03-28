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
                .Where(e => e.Value != 0)
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
                _values[i] = (ZoneAttributes)(ushort)(response.SectionData[i * 2] << 8 | response.SectionData[i * 2 + 1]);
        }
    }

    public async Task ReadAsync(SendSectionRead send, int zone, CancellationToken ct)
    {
        var response = await send(new SectionRead { SectionAddress = [2, (ushort)zone] }, ct);
        if (response?.SectionData is { Length: >= 2 })
            _values[zone - 1] = (ZoneAttributes)(ushort)(response.SectionData[0] << 8 | response.SectionData[1]);
    }
}

[Flags]
public enum ZoneAttributes : ushort
{
    IsAudible = 1,
    IsPulsedOrSteady = 2,
    IsDoorChime = IsPulsedOrSteady | IsAudible,
    IsBypassable = 4,
    IsForceArmable = IsBypassable | IsAudible,
    IsSwingerShutdown = IsBypassable | IsPulsedOrSteady,
    IsTransmissionDelay = IsSwingerShutdown | IsAudible,
    IsBurglaryVerified = 8,
    IsNormallyClosedLoop = IsBurglaryVerified | IsAudible,
    IsSingleEOLResistor = IsBurglaryVerified | IsPulsedOrSteady,
    IsDoubleEOLResistor = IsSingleEOLResistor | IsAudible,
    IsFastLoopResponse = IsBurglaryVerified | IsBypassable,
    IsTwoWayAudio = IsFastLoopResponse | IsAudible,
    IsHoldupVerified = IsFastLoopResponse | IsPulsedOrSteady,
}
