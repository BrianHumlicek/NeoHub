using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR
{
    /// <summary>
    /// Proactively disconnects all active ITv2 sessions when the host is shutting down,
    /// ensuring END_SESSION messages are sent before Kestrel tears down connections.
    /// </summary>
    internal class SessionShutdownService : IHostedService
    {
        private readonly IITv2SessionManager _sessionManager;
        private readonly ILogger<SessionShutdownService> _logger;

        public SessionShutdownService(IITv2SessionManager sessionManager, ILogger<SessionShutdownService> logger)
        {
            _sessionManager = sessionManager;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Application shutting down — disconnecting all sessions");
            try
            {
                await _sessionManager.DisconnectAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting sessions during shutdown");
            }
        }
    }
}
