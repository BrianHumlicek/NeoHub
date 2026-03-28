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
        /// Default access code for one-touch arm/disarm operations (optional).
        /// If set, allows arming and disarming without entering a code each time.
        /// </summary>
        [Display(
            Name = "Default Access Code",
            Description = "Optional access code for one-touch arm/disarm/bypass (leave empty to require code entry)",
            GroupName = "Panel Control",
            Order = 1)]
        [RegularExpression(@"^\d*$", ErrorMessage = "Access code must contain digits only (0–9)")]
        public string? DefaultAccessCode { get; set; }

        /// <summary>
        /// Default installer code for automatic panel configuration reading on connect (optional).
        /// If set, the full installer configuration (zone definitions, attributes, etc.) is
        /// read automatically when a panel session connects.
        /// </summary>
        [Display(
            Name = "Default Installer Code",
            Description = "Optional installer code for automatic configuration pull on connect (leave empty to skip)",
            GroupName = "Panel Control",
            Order = 2)]
        [RegularExpression(@"^\d*$", ErrorMessage = "Installer code must contain digits only (0–9)")]
        public string? DefaultInstallerCode { get; set; }

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
