namespace NeoHub.Services.PanelConfiguration.Sections;

/// <summary>
/// Zone type definitions from installer programming section [001].
/// Address: [001][zone] — one byte per zone.
/// </summary>
public class ZoneDefinitionSection(PanelCapabilities capabilities)
    : SectionGroup<ZoneDefinition>(capabilities)
{
    public override string DisplayName => "Zone Definition";
    public override int MaxItems => Capabilities.MaxZones;

    protected override ushort[] GetItemAddress(int item) => [1, (ushort)item];

    protected override ZoneDefinition[] DeserializeAll(byte[] data, int count)
    {
        var result = new ZoneDefinition[count];
        for (int i = 0; i < Math.Min(data.Length, count); i++)
            result[i] = (ZoneDefinition)data[i];
        return result;
    }

    protected override byte[] SerializeAll(ZoneDefinition[] values)
        => values.Select(v => (byte)v).ToArray();

    public override string FormatItemValue(int item)
    {
        var def = this[item];
        if (def == ZoneDefinition.NullZone)
            return "";

        var raw = (byte)def;
        var bits = Convert.ToString(raw, 2).PadLeft(8, '0');
        return $"{raw:X2} [{bits[..4]} {bits[4..]}] {def}";
    }

    /// <summary>Snapshot of configured zones (filters out NullZone), 1-indexed.</summary>
    public override IReadOnlyList<(int Number, ZoneDefinition Value)> Items =>
        base.Items.Where(e => e.Value != ZoneDefinition.NullZone).ToList();
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
