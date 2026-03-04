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

            // Migrate legacy settings format if needed (safe to remove once all deployments are migrated)
            SettingsMigration.MigrateIfNeeded(builder.Environment.ContentRootPath);

            // Load user settings from persist folder (overrides appsettings.json)
            // AddJsonFile resolves relative paths from ContentRootPath
            builder.Configuration.AddJsonFile(
                SettingsPersistenceService.SettingsFileRelativePath,
                optional: true, 
                reloadOnChange: true);

            // Register settings
            builder.Services.Configure<PanelConnectionsSettings>(
                builder.Configuration.GetSection(PanelConnectionsSettings.SectionName));
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
            builder.Services.AddSingleton<IConnectionSettingsProvider, ConnectionSettingsProvider>();

            // WebSocket API
            builder.Services.AddSingleton<PanelWebSocketHandler>();

            // TLink infrastructure
            var listenPort = builder.Configuration.GetValue(
                $"{ApplicationSettings.SectionName}:{nameof(ApplicationSettings.ListenPort)}",
                ConnectionSettings.DefaultListenPort);
            builder.UseITv2(listenPort);

            // Configure Kestrel — web UI ports
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                var httpPort = context.Configuration.GetValue("HttpPort", 8080);
                var httpsPort = context.Configuration.GetValue("HttpsPort", 8443);
                var enableHttps = context.Configuration.GetValue("EnableHttps", false);

                options.ListenAnyIP(httpPort);

                if (enableHttps)
                {
                    options.ListenAnyIP(httpsPort, listenOptions => listenOptions.UseHttps());
                }
            });

            // Add MudBlazor services
            builder.Services.AddMudServices();

            var app = builder.Build();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("NeoHub build {buildNumber} GitHub hash {hash} build datetime UTC {buildDateTime}", BuildInfo.BuildNumber, BuildInfo.GitCommit,  BuildInfo.BuildTimeUtc);
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
