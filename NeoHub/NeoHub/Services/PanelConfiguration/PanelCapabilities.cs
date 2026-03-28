namespace NeoHub.Services.PanelConfiguration;

/// <summary>
/// Panel capabilities extracted from ConnectionSystemCapabilities.
/// Passed to section classes so they know their element counts
/// and can adapt to firmware differences in the future.
/// </summary>
public record PanelCapabilities
{
    public required int MaxZones { get; init; }
    public required int MaxPartitions { get; init; }
    public required int MaxUsers { get; init; }
    public required int MaxFOBs { get; init; }
    public required int MaxProxTags { get; init; }
    public required int MaxOutputs { get; init; }
}
