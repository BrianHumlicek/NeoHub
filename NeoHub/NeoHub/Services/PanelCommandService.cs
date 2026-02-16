using DSC.TLink.ITv2;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Options;
using NeoHub.Services.Settings;

namespace NeoHub.Services
{
    public class PanelCommandService : IPanelCommandService
    {
        private readonly IMediator _mediator;
        private readonly ILogger<PanelCommandService> _logger;
        private readonly IOptionsMonitor<ApplicationSettings> _settings;

        public PanelCommandService(
            IMediator mediator, 
            ILogger<PanelCommandService> logger,
            IOptionsMonitor<ApplicationSettings> settings)
        {
            _mediator = mediator;
            _logger = logger;
            _settings = settings;
        }

        public async Task<PanelCommandResult> ArmAsync(string sessionId, byte partition, ArmingMode mode, string? accessCode = null)
        {
            var code = accessCode ?? _settings.CurrentValue.DefaultAccessCode ?? string.Empty;

            _logger.LogInformation(
                "Arm command: Session={SessionId}, Partition={Partition}, Mode={Mode}, UsingDefaultCode={UsingDefault}",
                sessionId, partition, mode, string.IsNullOrEmpty(accessCode) && !string.IsNullOrEmpty(_settings.CurrentValue.DefaultAccessCode));

            var message = new PartitionArm
            {
                Partition = partition,
                ArmMode = mode,
                AccessCode = code
            };

            return await SendCommandAsync(sessionId, message);
        }

        public async Task<PanelCommandResult> DisarmAsync(string sessionId, byte partition, string? accessCode = null)
        {
            var code = accessCode ?? _settings.CurrentValue.DefaultAccessCode;

            if (string.IsNullOrEmpty(code))
            {
                return PanelCommandResult.Error("Access code is required to disarm");
            }

            _logger.LogInformation(
                "Disarm command: Session={SessionId}, Partition={Partition}, UsingDefaultCode={UsingDefault}",
                sessionId, partition, string.IsNullOrEmpty(accessCode) && !string.IsNullOrEmpty(_settings.CurrentValue.DefaultAccessCode));

            var message = new PartitionDisarm
            {
                Partition = partition,
                AccessCode = code
            };

            return await SendCommandAsync(sessionId, message);
        }

        private async Task<PanelCommandResult> SendCommandAsync(string sessionId, IMessageData message)
        {
            try
            {
                SessionResponse response = await _mediator.Send(new SessionCommand
                {
                    SessionID = sessionId,
                    MessageData = message
                });

                if (response.Success)
                    return PanelCommandResult.Ok();

                _logger.LogWarning("Command failed: {Error} {detail}", response.ErrorMessage, response.ErrorDetail);
                string errorMessage = response.ErrorDetail ?? response.ErrorMessage ?? "Unknown error";
                return PanelCommandResult.Error(errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending command to session {SessionId}", sessionId);
                return PanelCommandResult.Error(ex.Message);
            }
        }
    }
}
