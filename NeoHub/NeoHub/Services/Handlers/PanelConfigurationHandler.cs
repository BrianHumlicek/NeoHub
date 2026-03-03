using DSC.TLink.ITv2;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Options;
using NeoHub.Services.Models;
using NeoHub.Services.Settings;

namespace NeoHub.Services.Handlers
{
    /// <summary>
    /// Triggered when a panel session connects. Pulls system capabilities, labels,
    /// and initial partition/zone status, then populates PanelStateService.
    /// </summary>
    public class PanelConfigurationHandler : INotificationHandler<SessionConnectedNotification>
    {
        private const int MaxLabelsPerRequest = 4;

        private readonly IMediator _mediator;
        private readonly IPanelStateService _panelState;
        private readonly IOptionsMonitor<PanelConnectionsSettings> _connectionSettings;
        private readonly ILogger<PanelConfigurationHandler> _logger;

        public PanelConfigurationHandler(
            IMediator mediator,
            IPanelStateService panelState,
            IOptionsMonitor<PanelConnectionsSettings> connectionSettings,
            ILogger<PanelConfigurationHandler> logger)
        {
            _mediator = mediator;
            _panelState = panelState;
            _connectionSettings = connectionSettings;
            _logger = logger;
        }

        public async Task Handle(SessionConnectedNotification notification, CancellationToken cancellationToken)
        {
            var sessionId = notification.SessionId;
            _logger.LogInformation("Starting panel configuration pull for session {SessionId}", sessionId);

            try
            {
                var capabilities = await RequestAsync<ConnectionSystemCapabilities>(
                    sessionId,
                    new CommandRequestMessage { Request = new ConnectionSystemCapabilities() },
                    cancellationToken);

                if (capabilities == null)
                {
                    _logger.LogWarning("Failed to get system capabilities for session {SessionId}, aborting config pull", sessionId);
                    return;
                }

                _logger.LogInformation(
                    "Session {SessionId} capabilities: {MaxZones} zones, {MaxPartitions} partitions",
                    sessionId, capabilities.MaxZones, capabilities.MaxPartitions);

                _panelState.UpdateSession(sessionId, session =>
                {
                    session.MaxZones = capabilities.MaxZones;
                    session.MaxPartitions = capabilities.MaxPartitions;
                });

                var connectionSettings = _connectionSettings.CurrentValue.Connections
                    .FirstOrDefault(c => string.Equals(c.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
                var maxZonesSetting = connectionSettings?.MaxZones ?? 0;
                var effectiveZones = maxZonesSetting > 0
                    ? Math.Min(capabilities.MaxZones, maxZonesSetting)
                    : capabilities.MaxZones;

                _logger.LogInformation(
                    "Session {SessionId}: using {EffectiveZones} zones (panel max: {PanelMax}, setting: {Setting})",
                    sessionId, effectiveZones, capabilities.MaxZones, maxZonesSetting > 0 ? maxZonesSetting : "unlimited");

                await PullZoneLabelsAsync(sessionId, effectiveZones, cancellationToken);
                await PullPartitionStatusAsync(sessionId, capabilities.MaxPartitions, cancellationToken);
                await PullPartitionLabelsAsync(sessionId, capabilities.MaxPartitions, cancellationToken);
                await PullZoneStatusAsync(sessionId, effectiveZones, cancellationToken);

                _panelState.OnConfigurationComplete(sessionId);
                _logger.LogInformation("Panel configuration pull complete for session {SessionId}", sessionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Panel configuration pull cancelled for session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during panel configuration pull for session {SessionId}", sessionId);
            }
        }

        private async Task PullZoneLabelsAsync(string sessionId, int maxZones, CancellationToken ct)
        {
            for (int start = 1; start <= maxZones; start += MaxLabelsPerRequest)
            {
                int end = Math.Min(start + MaxLabelsPerRequest - 1, maxZones);

                var response = await RequestAsync<NotificationLabelText>(
                    sessionId,
                    new CommandRequestMessage
                    {
                        Request = new NotificationLabelText { Unknown = 0xD1, Start = start, End = end }
                    },
                    ct);

                if (response == null)
                {
                    _logger.LogWarning("Failed to get zone labels {Start}-{End} for session {SessionId}",
                        start, end, sessionId);
                }
            }

            _logger.LogInformation("Pulled zone labels for session {SessionId}", sessionId);
        }

        private async Task PullPartitionLabelsAsync(string sessionId, int maxPartitions, CancellationToken ct)
        {
            for (int start = 1; start <= maxPartitions; start += MaxLabelsPerRequest)
            {
                int end = Math.Min(start + MaxLabelsPerRequest - 1, maxPartitions);

                var response = await RequestAsync<NotificationLabelText>(
                    sessionId,
                    new CommandRequestMessage
                    {
                        Request = new NotificationLabelText { Unknown = 0xD3, Start = start, End = end }
                    },
                    ct);

                if (response == null)
                {
                    _logger.LogWarning("Failed to get partition labels {Start}-{End} for session {SessionId}",
                        start, end, sessionId);
                }
            }

            _logger.LogInformation("Pulled partition labels for session {SessionId}", sessionId);
        }

        private async Task PullPartitionStatusAsync(string sessionId, int maxPartitions, CancellationToken ct)
        {
            for (int partition = 1; partition <= maxPartitions; partition++)
            {
                var response = await RequestAsync<ModulePartitionStatus>(
                    sessionId,
                    new CommandRequestMessage
                    {
                        Request = new ModulePartitionStatus { Partition = partition }
                    },
                    ct);

                if (response == null)
                {
                    _logger.LogWarning("Failed to get status for partition {Partition} session {SessionId}",
                        partition, sessionId);
                    continue;
                }

                var partitionNumber = (byte)response.Partition;
                var state = _panelState.GetPartition(sessionId, partitionNumber)
                    ?? new PartitionState { PartitionNumber = partitionNumber };

                var s1 = response.Status1;
                var s2 = response.Status2;

                // Map flags to status — check in priority order
                if (s2.HasFlag(ModulePartitionStatus.PartitionStatus2.PartitionInAlarm))
                {
                    state.Status = PartitionStatus.Triggered;
                }
                else if (s1.HasFlag(ModulePartitionStatus.PartitionStatus1.EntryDelayInProgress))
                {
                    state.Status = PartitionStatus.Pending;
                }
                else if (s1.HasFlag(ModulePartitionStatus.PartitionStatus1.StayArmed))
                {
                    state.Status = PartitionStatus.ArmedHome;
                }
                else if (s1.HasFlag(ModulePartitionStatus.PartitionStatus1.NightArmed))
                {
                    state.Status = PartitionStatus.ArmedNight;
                }
                else if (s1.HasFlag(ModulePartitionStatus.PartitionStatus1.Armed))
                {
                    state.Status = PartitionStatus.ArmedAway;
                }
                else
                {
                    state.Status = PartitionStatus.Disarmed;
                }

                state.IsReady = !s1.HasFlag(ModulePartitionStatus.PartitionStatus1.Armed)
                    && (s1.HasFlag(ModulePartitionStatus.PartitionStatus1.ReadyToArm)
                        || s1.HasFlag(ModulePartitionStatus.PartitionStatus1.ReadyToForceArm));

                if (s1.HasFlag(ModulePartitionStatus.PartitionStatus1.ExitDelayInProgress))
                {
                    if (!state.ExitDelayActive)
                    {
                        state.ExitDelayActive = true;
                        state.ExitDelayStartedAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    state.ExitDelayActive = false;
                    state.ExitDelayStartedAt = null;
                }

                state.LastUpdated = DateTime.UtcNow;
                _panelState.UpdatePartition(sessionId, state);

                _logger.LogDebug("Partition {Partition} Status1=0x{S1:X} Status2=0x{S2:X} → {Status}, IsReady={IsReady} (Session: {SessionId})",
                    partitionNumber, (byte)s1, (byte)s2, state.Status, state.IsReady, sessionId);
            }

            _logger.LogInformation("Pulled initial partition status for session {SessionId}", sessionId);
        }

        private async Task PullZoneStatusAsync(string sessionId, int maxZones, CancellationToken ct)
        {
            var response = await RequestAsync<ModuleZoneStatus>(
                sessionId,
                new CommandRequestMessage
                {
                    Request = new ModuleZoneStatus { ZoneStart = 1, ZoneCount = maxZones }
                },
                ct);

            if (response == null)
            {
                _logger.LogWarning("Failed to get zone status for session {SessionId}", sessionId);
                return;
            }

            for (int i = 0; i < response.ZoneStatusBytes.Length; i++)
            {
                var zoneNumber = (byte)(response.ZoneStart + i);
                var status = response.ZoneStatusBytes[i];

                var zone = _panelState.GetZone(sessionId, zoneNumber)
                    ?? new ZoneState { ZoneNumber = zoneNumber };

                zone.IsOpen = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Open);
                zone.IsFaulted = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Fault);
                zone.IsTampered = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Tamper);
                zone.IsBypassed = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Bypass);
                zone.LastUpdated = DateTime.UtcNow;

                _panelState.UpdateZone(sessionId, zone);
            }

            _logger.LogInformation("Pulled initial status for {Count} zones for session {SessionId}",
                response.ZoneStatusBytes.Length, sessionId);
        }

        private async Task<T?> RequestAsync<T>(string sessionId, IMessageData request, CancellationToken ct)
            where T : class, IMessageData
        {
            var response = await _mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = request
            }, ct);

            if (!response.Success)
            {
                _logger.LogWarning("Command {Command} failed for session {SessionId}: {Error}",
                    typeof(T).Name, sessionId, response.ErrorMessage);
                return null;
            }

            return response.MessageData as T;
        }
    }
}
