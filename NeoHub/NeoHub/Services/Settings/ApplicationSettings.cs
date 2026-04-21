using System.ComponentModel.DataAnnotations;
using DSC.TLink.ITv2;

namespace NeoHub.Services.Settings
{
    /// <summary>
    /// Application-level settings for user preferences and panel control options.
    /// Binds to the "Application" section in appsettings.json / userSettings.json
    /// </summary>
    [Display(Name = "Application Settings", Description = "User preferences and panel control options")]
    public class ApplicationSettings
    {
        public const string SectionName = "Application";

        /// <summary>
        /// TCP port for panel connections (default: 3072)
        /// </summary>
        [Display(
            Name = "Server Port",
            Description = "TCP port for panel connections",
            GroupName = "Network",
            Order = 10)]
        [Range(1, 65535)]
        public int ListenPort { get; set; } = ConnectionSettings.DefaultListenPort;
    }
}
