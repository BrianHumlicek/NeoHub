using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;

namespace NeoHub.Services.PanelConfiguration;

public class PanelConfigurationService : IPanelConfigurationService
{
    private readonly IMediator _mediator;
    private readonly IPanelStateService _panelState;
    private readonly ILogger<PanelConfigurationService> _logger;

    public PanelConfigurationService(
        IMediator mediator,
        IPanelStateService panelState,
        ILogger<PanelConfigurationService> logger)
    {
        _mediator = mediator;
        _panelState = panelState;
        _logger = logger;
    }

    public async Task<ConfigReadResult> ReadAllAsync(string sessionId, string installerCode, CancellationToken ct)
    {
        var session = _panelState.GetSession(sessionId);
        if (session == null)
            return new(false, "Session not found");

        // If already in programming mode, skip entering config mode
        bool enteredConfigMode = false;
        if (!session.IsInProgrammingMode)
        {
            var enterResult = await EnterConfigModeAsync(sessionId, installerCode, ct);
            if (!enterResult.Success)
                return enterResult;
            enteredConfigMode = true;
        }
        else
        {
            _logger.LogDebug("Panel already in programming mode, skipping config mode entry");
        }

        try
        {
            var capabilities = new PanelCapabilities
            {
                MaxZones = session.MaxZones,
                MaxPartitions = session.MaxPartitions,
                MaxUsers = 0,
                MaxFOBs = 0,
                MaxProxTags = 0,
                MaxOutputs = 0,
            };
            var config = new PanelConfigurationState(capabilities);

            SendSectionRead send = async (request, token) =>
            {
                var response = await _mediator.Send(new SessionCommand
                {
                    SessionID = sessionId,
                    MessageData = request
                }, token);

                return response.Success ? response.MessageData as SectionReadResponse : null;
            };

            _logger.LogInformation("Reading panel configuration sections");

            await ReadSectionAsync("zone definitions", () => config.ZoneDefinitions.ReadAllAsync(send, ct));
            await ReadSectionAsync("zone attributes", () => config.ZoneAttributes.ReadAllAsync(send, ct));
            await ReadSectionAsync("partition enables", () => config.PartitionEnables.ReadAllAsync(send, ct));
            await ReadSectionAsync("zone assignments", () => config.ZoneAssignments.ReadAllAsync(send, ct));
            await ReadSectionAsync("zone labels", () => config.ZoneLabels.ReadAllAsync(send, ct));
            await ReadSectionAsync("partition labels", () => config.PartitionLabels.ReadAllAsync(send, ct));

            config.LastReadAt = DateTime.UtcNow;

            _panelState.UpdateSession(sessionId, s => s.Configuration = config);
            _logger.LogInformation("Panel configuration read complete");
            return new(true);
        }
        finally
        {
            if (enteredConfigMode)
                await ExitConfigModeAsync(sessionId, ct);
        }
    }

    private async Task ReadSectionAsync(string name, Func<Task> read)
    {
        try
        {
            await read();
            _logger.LogDebug("Read {Section}", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {Section}", name);
        }
    }

    private async Task<ConfigReadResult> EnterConfigModeAsync(string sessionId, string installerCode, CancellationToken ct)
    {
        try
        {
            var response = await _mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = new ConfigurationEnter
                {
                    Partition = 1,
                    ProgrammingMode = ProgrammingMode.InstallersProgramming,
                    AccessCode = installerCode,
                    ReadWrite = ConfigurationEnter.ReadWriteAccessEnum.ReadOnlyMode
                }
            }, ct);

            if (!response.Success)
            {
                _logger.LogWarning("Failed to enter config mode: {Error}", response.ErrorMessage);
                return new(false, response.ErrorMessage ?? "Failed to enter config mode");
            }

            _logger.LogDebug("Entered installer config mode");
            return new(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error entering config mode");
            return new(false, ex.Message);
        }
    }

    private async Task ExitConfigModeAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = new ConfigurationExit { Partition = 0 }
            }, ct);

            _logger.LogDebug("Exited config mode");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to exit config mode");
        }
    }
}
