using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace NeoHub.Services.Settings
{
    /// <summary>
    /// Discovers all IOptions registrations in the service collection
    /// and extracts metadata for UI generation
    /// </summary>
    public interface ISettingsDiscoveryService
    {
        IReadOnlyList<SettingsSectionMetadata> DiscoveredSettings { get; }
    }

    public class SettingsDiscoveryService : ISettingsDiscoveryService
    {
        private readonly List<SettingsSectionMetadata> _discoveredSettings = new();
        private readonly ILogger<SettingsDiscoveryService> _log;

        public IReadOnlyList<SettingsSectionMetadata> DiscoveredSettings => _discoveredSettings.AsReadOnly();

        public SettingsDiscoveryService(IServiceProvider serviceProvider, ILogger<SettingsDiscoveryService> log)
        {
            _log = log;
            DiscoverSettings(serviceProvider);
        }

        private void DiscoverSettings(IServiceProvider serviceProvider)
        {
            // Scan only your local project assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && 
                       (a.FullName!.StartsWith("NeoHub") || 
                        a.FullName.StartsWith("DSC.TLink")))
                .ToList();

            _log.LogInformation("Scanning {Count} assemblies for settings types", assemblies.Count);

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract
                            && t.GetCustomAttribute<DisplayAttribute>() != null
                            && HasOptionsRegistration(serviceProvider, t));

                    foreach (var type in types)
                    {
                        try
                        {
                            var metadata = ExtractMetadata(type);
                            if (metadata == null)
                            {
                                _log.LogDebug("Skipping settings type {Type} (no renderable properties)", type.Name);
                                continue;
                            }
                            _discoveredSettings.Add(metadata);
                            _log.LogInformation("Discovered settings: {Section} ({Type})", 
                                metadata.SectionName, type.Name);
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to extract metadata for settings type {Type}", type.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Error scanning assembly {Assembly} for settings", assembly.FullName);
                }
            }

            _log.LogInformation("Discovered {Count} settings sections", _discoveredSettings.Count);
        }

        private bool HasOptionsRegistration(IServiceProvider serviceProvider, Type settingsType)
        {
            try
            {
                // Check if there's an actual IConfigureOptions<T> registered for this type
                var configureOptionsType = typeof(IConfigureOptions<>).MakeGenericType(settingsType);
                var configureOptions = serviceProvider.GetService(configureOptionsType);
                return configureOptions != null;
            }
            catch
            {
                return false;
            }
        }

        private SettingsSectionMetadata? ExtractMetadata(Type settingsType)
        {
            // Try to find SectionName from a const field
            var sectionNameField = settingsType.GetField("SectionName", BindingFlags.Public | BindingFlags.Static);
            var sectionName = sectionNameField?.GetValue(null)?.ToString() ?? settingsType.Name;

            // Try to get display info from type-level Display attribute
            var typeDisplay = settingsType.GetCustomAttribute<DisplayAttribute>();

            var metadata = new SettingsSectionMetadata
            {
                SettingsType = settingsType,
                SectionName = sectionName,
                DisplayName = typeDisplay?.Name ?? AddSpacesToPascalCase(settingsType.Name),
                Description = typeDisplay?.Description,
                GroupName = typeDisplay?.GroupName
            };

            // Extract properties — only include types the generic Settings page can render
            var properties = settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && IsRenderableType(p.PropertyType))
                .Select(ExtractPropertyMetadata)
                .OrderBy(p => p.Order)
                .ThenBy(p => p.DisplayName)
                .ToList();

            if (properties.Count == 0)
                return null;

            metadata.Properties.AddRange(properties);

            return metadata;
        }

        private static bool IsRenderableType(Type type) =>
            type == typeof(string) || type == typeof(int) || type == typeof(bool) || type.IsEnum;

        private SettingsPropertyMetadata ExtractPropertyMetadata(PropertyInfo property)
        {
            var displayAttr = property.GetCustomAttribute<DisplayAttribute>();
            var requiredAttr = property.GetCustomAttribute<RequiredAttribute>();

            return new SettingsPropertyMetadata
            {
                PropertyName = property.Name,
                DisplayName = displayAttr?.GetName() ?? AddSpacesToPascalCase(property.Name),
                Description = displayAttr?.GetDescription(),
                GroupName = displayAttr?.GetGroupName(),
                PropertyType = property.PropertyType,
                Order = displayAttr?.GetOrder() ?? int.MaxValue,
                DefaultValue = GetDefaultValue(property),
                IsRequired = requiredAttr != null,
                IsSensitive = IsPasswordOrSensitive(property.Name)
            };
        }

        private object? GetDefaultValue(PropertyInfo property)
        {
            try
            {
                var instance = Activator.CreateInstance(property.DeclaringType!);
                return property.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private bool IsPasswordOrSensitive(string propertyName)
        {
            return false;   //I dont want password/sensitive fields at this time.
            var sensitiveKeywords = new[] { "password", "secret", "key", "token", "code" };
            return sensitiveKeywords.Any(k => propertyName.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private static string AddSpacesToPascalCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var result = new System.Text.StringBuilder();
            result.Append(text[0]);
            for (int i = 1; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]) && !char.IsUpper(text[i - 1]))
                    result.Append(' ');
                result.Append(text[i]);
            }
            return result.ToString();
        }
    }
}