using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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
        private readonly ConcurrentDictionary<string, LogLevel> _configLevelCache = new();
        private readonly IDisposable? _configChangeRegistration;

        public DiagnosticsLoggerProvider(
            IDiagnosticsLogService diagnosticsService,
            IOptionsMonitor<DiagnosticsSettings> settings,
            IConfiguration configuration)
        {
            _diagnosticsService = diagnosticsService;
            _settings = settings;
            _configuration = configuration;

            _configChangeRegistration = ChangeToken.OnChange(
                () => _configuration.GetReloadToken(),
                () => _configLevelCache.Clear());
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new DiagnosticsLogger(categoryName, _diagnosticsService, _settings, _configuration, _configLevelCache);
        }

        public void Dispose()
        {
            _configChangeRegistration?.Dispose();
        }

        private class DiagnosticsLogger : ILogger
        {
            private readonly string _category;
            private readonly bool _isSolutionCategory;
            private readonly IDiagnosticsLogService _diagnosticsService;
            private readonly IOptionsMonitor<DiagnosticsSettings> _settings;
            private readonly IConfiguration _configuration;
            private readonly ConcurrentDictionary<string, LogLevel> _configLevelCache;

            public DiagnosticsLogger(
                string category,
                IDiagnosticsLogService diagnosticsService,
                IOptionsMonitor<DiagnosticsSettings> settings,
                IConfiguration configuration,
                ConcurrentDictionary<string, LogLevel> configLevelCache)
            {
                _category = category;
                _isSolutionCategory = IsSolutionCategory(category);
                _diagnosticsService = diagnosticsService;
                _settings = settings;
                _configuration = configuration;
                _configLevelCache = configLevelCache;
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
                if (!_isSolutionCategory)
                {
                    var configFloor = _configLevelCache.GetOrAdd(_category, ResolveConfigLevelCore, _configuration);
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
            private static LogLevel ResolveConfigLevelCore(string category, IConfiguration configuration)
            {
                var section = configuration.GetSection("Logging:LogLevel");

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
                foreach (var prefix in SolutionPrefixes)
                {
                    if (category.StartsWith(prefix, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
        }
    }
}