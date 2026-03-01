using System.ComponentModel.DataAnnotations;

namespace NeoHub.Services.Diagnostics
{
    [Display(Name = "Diagnostics", Description = "Log viewer and diagnostics settings")]
    public class DiagnosticsSettings
    {
        public const string SectionName = "Diagnostics";

        [Display(
            Name = "Minimum Log Level",
            Description = "Global minimum log level for capture and display",
            Order = 1)]
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

        [Display(
            Name = "Max Log Entries",
            Description = "Maximum number of log entries to retain in the buffer",
            Order = 2)]
        [Range(100, 100_000)]
        public int MaxLogEntries { get; set; } = 5000;

        /// <summary>
        /// Per-category log level overrides. Managed from the Diagnostics page.
        /// Key = full category name, Value = minimum log level for that category.
        /// </summary>
        public Dictionary<string, LogLevel> CategoryOverrides { get; set; } = new();
    }
}