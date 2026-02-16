using DSC.TLink;
using DSC.TLink.ITv2;
using NeoHub.Components;
using NeoHub.Services;
using NeoHub.Services.Settings;
using NeoHub.Services.Diagnostics;
using NeoHub.Api.WebSocket;
using MudBlazor.Services;

namespace NeoHub
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Load user settings from persist folder (overrides appsettings.json)
            // AddJsonFile resolves relative paths from ContentRootPath
            builder.Configuration.AddJsonFile(
                SettingsPersistenceService.SettingsFileRelativePath,
                optional: true, 
                reloadOnChange: true);

            // Register settings
            builder.Services.Configure<ITv2Settings>(
                builder.Configuration.GetSection(ITv2Settings.SectionName));
            builder.Services.Configure<DiagnosticsSettings>(
                builder.Configuration.GetSection(DiagnosticsSettings.SectionName));
            builder.Services.Configure<ApplicationSettings>(
                builder.Configuration.GetSection(ApplicationSettings.SectionName));

            // Register settings services
            builder.Services.AddSingleton<ISettingsDiscoveryService, SettingsDiscoveryService>();
            builder.Services.AddSingleton<ISettingsPersistenceService, SettingsPersistenceService>();

            // Diagnostics log service (must be before logging configuration)
            builder.Services.AddSingleton<IDiagnosticsLogService, DiagnosticsLogService>();

            // Add custom logging provider
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.Services.AddSingleton<ILoggerProvider, DiagnosticsLoggerProvider>();

            // Allow Trace-level logs to reach the DiagnosticsLoggerProvider.
            // The provider does its own filtering via DiagnosticsSettings.MinimumLogLevel.
            // Without this, the framework's default "Information" floor discards Trace/Debug
            // before they ever reach the provider.
            builder.Logging.AddFilter<DiagnosticsLoggerProvider>(null, LogLevel.Trace);

            // Add Blazor services
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // MediatR
            builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(
                typeof(Program).Assembly,
                typeof(StartupExtensions).Assembly));

            // Application services
            builder.Services.AddSingleton<IPanelStateService, PanelStateService>();
            builder.Services.AddSingleton<IPanelCommandService, PanelCommandService>();
            builder.Services.AddSingleton<ISessionMonitor, SessionMonitor>();

            // WebSocket API
            builder.Services.AddSingleton<PanelWebSocketHandler>();

            // TLink infrastructure
            builder.UseITv2();

            // Add MudBlazor services
            builder.Services.AddMudServices();

            var app = builder.Build();

            // Force initialization
            app.Services.GetRequiredService<ISettingsDiscoveryService>();

            app.UseWebSockets();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseAntiforgery();

            app.Map("/api/ws", async context =>
            {
                var handler = context.RequestServices.GetRequiredService<PanelWebSocketHandler>();
                await handler.HandleConnectionAsync(context);
            });

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
