using NeoHub.Services.PanelConfiguration.Sections;

namespace NeoHub.Services.PanelConfiguration;

/// <summary>
/// Holds all installer configuration data read from the panel via SectionRead.
/// One instance per session, stored on SessionState.Configuration.
/// Null on SessionState until a config read is explicitly triggered by the user.
/// </summary>
public class PanelConfigurationState
{
    public PanelCapabilities Capabilities { get; }

    public ZoneDefinitionSection ZoneDefinitions { get; }
    public ZoneAttributeSection ZoneAttributes { get; }
    public ZoneLabelSection ZoneLabels { get; }
    public ZoneAssignmentSection ZoneAssignments { get; }
    public PartitionLabelSection PartitionLabels { get; }
    public PartitionEnableSection PartitionEnables { get; }

    public DateTime? LastReadAt { get; set; }

    public PanelConfigurationState(PanelCapabilities capabilities)
    {
        Capabilities = capabilities;
        ZoneDefinitions = new(capabilities);
        ZoneAttributes = new(capabilities);
        ZoneLabels = new(capabilities);
        ZoneAssignments = new(capabilities);
        PartitionLabels = new(capabilities);
        PartitionEnables = new(capabilities);
    }
}
