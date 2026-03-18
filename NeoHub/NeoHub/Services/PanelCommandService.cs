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
                "Arm command: Partition={Partition}, Mode={Mode}, UsingDefaultCode={UsingDefault}",
                partition, mode, string.IsNullOrEmpty(accessCode) && !string.IsNullOrEmpty(_settings.CurrentValue.DefaultAccessCode));

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
                "Disarm command: Partition={Partition}, UsingDefaultCode={UsingDefault}",
                partition, string.IsNullOrEmpty(accessCode) && !string.IsNullOrEmpty(_settings.CurrentValue.DefaultAccessCode));

            var message = new PartitionDisarm
            {
                Partition = partition,
                AccessCode = code
            };

            return await SendCommandAsync(sessionId, message);
        }

        public async Task<PanelCommandResult> BypassZoneAsync(string sessionId, byte partition, byte zoneNumber, bool bypass, string? accessCode = null)
        {
            var code = accessCode ?? _settings.CurrentValue.DefaultAccessCode;

            if (string.IsNullOrEmpty(code))
                return PanelCommandResult.Error("Access code is required to bypass zones");

            _logger.LogInformation(
                "Bypass command: Partition={Partition}, Zone={Zone}, Bypass={Bypass}, UsingDefaultCode={UsingDefault}",
                partition, zoneNumber, bypass,
                string.IsNullOrEmpty(accessCode) && !string.IsNullOrEmpty(_settings.CurrentValue.DefaultAccessCode));

            var enterResult = await SendCommandAsync(sessionId, new ConfigurationEnter
            {
                Partition = partition,
                ProgrammingType = 0x03,
                AccessCode = ToRawDigitBytes(code),
                ReadWriteMode = 0x01,
            });

            if (!enterResult.Success)
            {
                _logger.LogWarning("Bypass failed: could not enter config mode. {Error}", enterResult.ErrorMessage);
                return enterResult;
            }

            PanelCommandResult bypassResult;
            try
            {
                bypassResult = await SendCommandAsync(sessionId, new SingleZoneBypassWrite
                {
                    Partition = partition,
                    ZoneNumber = zoneNumber,
                    BypassState = bypass ? (byte)0x01 : (byte)0x00,
                });
            }
            finally
            {
                var exitResult = await SendCommandAsync(sessionId, new ConfigurationExit { Partition = partition });
                if (!exitResult.Success)
                    _logger.LogWarning("Failed to exit config mode after bypass. {Error}", exitResult.ErrorMessage);
            }

            return bypassResult;
        }

        private static byte[] ToRawDigitBytes(string code)
            => code.Where(char.IsDigit).Select(c => (byte)(c - '0')).ToArray();

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

                _logger.LogWarning("Command failed: [{Code}] {Error}", response.ErrorCode, response.ErrorMessage);

                return response.ErrorCode.HasValue
                    ? PanelCommandResult.Error(response.ErrorCode.Value, response.ErrorMessage ?? response.ErrorCode.Value.ToString())
                    : PanelCommandResult.Error(response.ErrorMessage ?? "Unknown error");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending command");
                return PanelCommandResult.Error(ex.Message);
            }
        }
    }
}
