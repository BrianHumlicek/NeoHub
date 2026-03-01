using Microsoft.Extensions.Options;

namespace NeoHub.Services.Diagnostics
{
    /// <summary>
    /// Custom logger provider that feeds logs into the diagnostics service.
    /// Supports per-category log level overrides. Non-solution categories are
    /// clamped to the floor defined in appsettings.json Logging:LogLevel so
    /// verbose levels (Trace/Debug) only apply to solution code.
    /// </summary>
    public class DiagnosticsLoggerProvider : ILoggerProvider
    {
        private static readonly string[] SolutionPrefixes = ["NeoHub", "DSC.TLink"];

        private readonly IDiagnosticsLogService _diagnosticsService;
        private readonly IOptionsMonitor<DiagnosticsSettings> _settings;
        private readonly IConfiguration _configuration;

        public DiagnosticsLoggerProvider(
            IDiagnosticsLogService diagnosticsService,
            IOptionsMonitor<DiagnosticsSettings> settings,
            IConfiguration configuration)
        {
            _diagnosticsService = diagnosticsService;
            _settings = settings;
            _configuration = configuration;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DiagnosticsLogger(categoryName, _diagnosticsService, _settings, _configuration);
        }

        public void Dispose() { }

        private class DiagnosticsLogger : ILogger
        {
            private readonly string _category;
            private readonly IDiagnosticsLogService _diagnosticsService;
            private readonly IOptionsMonitor<DiagnosticsSettings> _settings;
            private readonly IConfiguration _configuration;

            public DiagnosticsLogger(
                string category,
                IDiagnosticsLogService diagnosticsService,
                IOptionsMonitor<DiagnosticsSettings> settings,
                IConfiguration configuration)
            {
                _category = category;
                _diagnosticsService = diagnosticsService;
                _settings = settings;
                _configuration = configuration;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel)
            {
                var settings = _settings.CurrentValue;

                // Per-category override takes precedence, otherwise use global minimum
                var effectiveLevel = settings.CategoryOverrides.TryGetValue(_category, out var categoryLevel)
                    ? categoryLevel
                    : settings.MinimumLogLevel;

                // Non-solution categories: clamp to the appsettings floor so
                // verbose levels only ever apply to solution code
                if (!IsSolutionCategory(_category))
                {
                    var configFloor = ResolveConfigLevel(_category);
                    effectiveLevel = (LogLevel)Math.Max((int)effectiveLevel, (int)configFloor);
                }

                return logLevel >= effectiveLevel;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                _diagnosticsService.AddLog(new DiagnosticsLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    LogLevel = logLevel,
                    Category = _category,
                    Message = formatter(state, exception),
                    Exception = exception
                });
            }

            /// <summary>
            /// Resolves the configured log level for a category by walking
            /// Logging:LogLevel with progressively shorter prefixes, matching
            /// the same precedence rules ASP.NET Core uses.
            /// </summary>
            private LogLevel ResolveConfigLevel(string category)
            {
                var section = _configuration.GetSection("Logging:LogLevel");

                var prefix = category;
                while (true)
                {
                    var value = section[prefix];
                    if (value != null && Enum.TryParse<LogLevel>(value, out var level))
                        return level;

                    var lastDot = prefix.LastIndexOf('.');
                    if (lastDot < 0)
                        break;
                    prefix = prefix[..lastDot];
                }

                var defaultValue = section["Default"];
                if (defaultValue != null && Enum.TryParse<LogLevel>(defaultValue, out var defaultLevel))
                    return defaultLevel;

                return LogLevel.Information;
            }

            private static bool IsSolutionCategory(string category)
            {
                return SolutionPrefixes.Any(p => category.StartsWith(p, StringComparison.Ordinal));
            }
        }
    }
}