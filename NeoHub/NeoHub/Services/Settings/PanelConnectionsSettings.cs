using DSC.TLink.ITv2;

namespace NeoHub.Services.Settings
{
    /// <summary>
    /// Server-side storage for panel connection configurations.
    /// Binds to the "PanelConnections" section in appsettings.json / userSettings.json
    /// </summary>
    public class PanelConnectionsSettings
    {
        public const string SectionName = "PanelConnections";

        /// <summary>
        /// Configured panel connections.
        /// Each entry is keyed by the panel's Integration Identification Number (SessionId).
        /// </summary>
        public List<ConnectionSettings> Connections { get; set; } = [];
    }
}
