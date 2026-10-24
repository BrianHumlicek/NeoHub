namespace NeoHub.Services.Settings
{
    /// <summary>
    /// User-defined display names for alarm panels.
    /// Names are keyed by the panel's Integration Identification Number (SessionId).
    /// </summary>
    public class PanelDisplaySettings
    {
        public const string SectionName = "PanelDisplay";

        public Dictionary<string, string> Names { get; set; } = new();
    }
}
