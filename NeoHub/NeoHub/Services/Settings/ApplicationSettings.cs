using System.ComponentModel.DataAnnotations;

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
            Description = "Optional access code for one-touch arm/disarm (leave empty to require code entry)",
            GroupName = "Panel Control",
            Order = 1)]
        public string? DefaultAccessCode { get; set; }
    }
}
