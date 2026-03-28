using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Zone type definitions from installer programming section [001].
/// Address: [001][zone] — one byte per zone.
/// </summary>
public class ZoneDefinitionSection
{
    private readonly PanelCapabilities _capabilities;
    private ZoneDefinition[] _values = [];

    public IReadOnlyList<ZoneDefinition> Values => _values;

    /// <summary>Snapshot of configured zones (filters out NullZone), 1-indexed.</summary>
    public IReadOnlyList<(int Number, ZoneDefinition Value)> Items
    {
        get
        {
            var values = _values;
            return values
                .Select((v, i) => (Number: i + 1, Value: v))
                .Where(e => e.Value != ZoneDefinition.NullZone)
                .ToList();
        }
    }

    public ZoneDefinitionSection(PanelCapabilities capabilities)
    {
        _capabilities = capabilities;
    }

    public async Task ReadAllAsync(SendSectionRead send, CancellationToken ct)
    {
        _values = new ZoneDefinition[_capabilities.MaxZones];

        var response = await send(new SectionRead { SectionAddress = [1, 1], Count = (byte)_capabilities.MaxZones }, ct);
        if (response?.SectionData is not null)
        {
            for (int i = 0; i < Math.Min(response.SectionData.Length, _values.Length); i++)
                _values[i] = (ZoneDefinition)response.SectionData[i];
        }
    }

    public async Task ReadAsync(SendSectionRead send, int zone, CancellationToken ct)
    {
        var response = await send(new SectionRead { SectionAddress = [1, (ushort)zone] }, ct);
        if (response?.SectionData is { Length: >= 1 })
            _values[zone - 1] = (ZoneDefinition)response.SectionData[0];
    }

    public async Task<SectionResult> WriteAsync(SendSectionWrite send, int zone, ZoneDefinition definition, CancellationToken ct)
    {
        var result = await send(new SectionWrite { SectionAddress = [1, (ushort)zone], SectionData = [(byte)definition] }, ct);
        if (result.Success)
            _values[zone - 1] = definition;
        return result;
    }
}

public enum ZoneDefinition : byte
{
    NullZone = 0,
    Delay1 = 1,
    Delay2 = 2,
    Instant = 3,
    Interior = 4,
    InteriorStayAway = 5,
    DelayStayAway = 6,
    Delayed24HourFire = 7,
    Standard24HourFire = 8,
    InstantStayAway = 9,
    InteriorDelay = 10,
    DayZone = 11,
    NightZone = 12,
    Burglary24Hour = 17,
    BellBuzzer24Hour = 18,
    Supervisory24Hour = 23,
    SupervisoryBuzzer24Hour = 24,
    AutoVerifiedFire = 25,
    FireSupervisory = 27,
    Gas24Hour = 40,
    CO24Hour = 41,
    Holdup24Hour = 42,
    Panic24Hour = 43,
    Heat24Hour = 45,
    Medical24Hour = 46,
    Emergency24Hour = 47,
    Sprinkler24Hour = 48,
    Flood24Hour = 49,
    LatchingTamper24Hour = 51,
    NonAlarm24Hour = 52,
    QuickBypass24Hour = 53,
    HighTemperature24Hour = 56,
    LowTemperature24Hour = 57,
    NonLatchingTamper24Hour = 60,
    MomentaryKeyswitchArm = 66,
    MaintainedKeyswitchArm = 67,
    MomentaryKeyswitchDisarm = 68,
    MaintainedKeyswitchDisarm = 69,
    DoorBell = 71,
}
