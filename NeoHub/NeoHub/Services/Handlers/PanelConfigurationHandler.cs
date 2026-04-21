using DSC.TLink.ITv2;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Options;
using NeoHub.Services.Models;
using NeoHub.Services.PanelConfiguration;
using NeoHub.Services.Settings;

namespace NeoHub.Services.Handlers
{
    /// <summary>
    /// Triggered when a panel session connects. Pulls system capabilities,
    /// then either reads full installer config (if installer code is available)
    /// or falls back to explicit label pulls. Always reads partition/zone status.
    /// </summary>
    public class PanelConfigurationHandler : INotificationHandler<SessionConnectedNotification>
    {
        private const int MaxLabelsPerRequest = 4;

        private readonly IMediator _mediator;
        private readonly IPanelStateService _panelState;
        private readonly IPanelConfigurationService _configService;
        private readonly IOptionsMonitor<PanelConnectionsSettings> _connectionSettings;
        private readonly ILogger<PanelConfigurationHandler> _logger;

        public PanelConfigurationHandler(
            IMediator mediator,
            IPanelStateService panelState,
            IPanelConfigurationService configService,
            IOptionsMonitor<PanelConnectionsSettings> connectionSettings,
            ILogger<PanelConfigurationHandler> logger)
        {
            _mediator = mediator;
            _panelState = panelState;
            _configService = configService;
            _connectionSettings = connectionSettings;
            _logger = logger;
        }

        public async Task Handle(SessionConnectedNotification notification, CancellationToken cancellationToken)
        {
            var sessionId = notification.SessionId;
            _logger.LogInformation("Starting panel configuration pull");

            // Ensure the session exists — it may not have been created yet
            // since the connected notification fires early in the lifecycle.
            _panelState.UpdateSession(sessionId, s => s.ConnectionPhase = ConnectionPhase.Handshake);
            var session = _panelState.GetSession(sessionId)!;

            // Acquire the config lock for the entire initialization sequence.
            // This prevents user-initiated operations from interleaving.
            await session.ConfigLock.WaitAsync(cancellationToken);

            try
            {
                // ── 1. Capabilities ──
                var capabilities = await RequestAsync<ConnectionSystemCapabilities>(
                    sessionId,
                    new CommandRequestMessage { Request = new ConnectionSystemCapabilities() },
                    cancellationToken);

                if (capabilities == null)
                {
                    _logger.LogWarning("Failed to get system capabilities, aborting config pull");
                    return;
                }

                _logger.LogInformation(
                    "Panel capabilities: {MaxZones} zones, {MaxPartitions} partitions, {MaxUsers} users",
                    capabilities.MaxZones, capabilities.MaxPartitions, capabilities.MaxUsers);

                _panelState.UpdateSession(sessionId, s =>
                {
                    s.MaxZones = capabilities.MaxZones;
                    s.MaxPartitions = capabilities.MaxPartitions;
                    s.MaxUsers = capabilities.MaxUsers;
                });

                var connectionSettings = _connectionSettings.CurrentValue.Connections
                    .FirstOrDefault(c => string.Equals(c.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
                var maxZonesSetting = connectionSettings?.MaxZones ?? 0;
                var effectiveZones = maxZonesSetting > 0
                    ? Math.Min(capabilities.MaxZones, maxZonesSetting)
                    : capabilities.MaxZones;

                _logger.LogInformation(
                    "Using {EffectiveZones} zones (panel max: {PanelMax}, setting: {Setting})",
                    effectiveZones, capabilities.MaxZones, maxZonesSetting > 0 ? maxZonesSetting : "unlimited");

                // ── 2. Labels: installer config vs. explicit pull ──
                _panelState.UpdateSession(sessionId, s => s.ConnectionPhase = ConnectionPhase.ReadingConfig);
                var installerCode = connectionSettings?.InstallerCode;
                if (!string.IsNullOrEmpty(installerCode))
                {
                    _logger.LogInformation("Installer code configured, reading panel configuration sections");
                    var result = await _configService.ExecuteInConfigModeAsync(
                        sessionId, installerCode, readWrite: false,
                        async () =>
                        {
                            await _configService.ReadSectionsAsync(
                                sessionId, CreateSendDelegate(sessionId), cancellationToken);
                            return new SectionResult(true);
                        }, cancellationToken);

                    if (result.Success)
                        ApplyConfigurationToSessionState(sessionId, effectiveZones, capabilities.MaxPartitions);
                    else
                        _logger.LogWarning("Installer config read failed: {Error}", result.ErrorMessage);
                }

                // Fall back to explicit label pull if no config was read
                session = _panelState.GetSession(sessionId)!;
                if (session.Configuration is null)
                {
                    await PullZoneLabelsAsync(sessionId, effectiveZones, cancellationToken);
                    await PullPartitionLabelsAsync(sessionId, capabilities.MaxPartitions, cancellationToken);
                }

                // ── 3. Status reads (always) ──
                _panelState.UpdateSession(sessionId, s => s.ConnectionPhase = ConnectionPhase.ReadingStatus);
                await PullPartitionStatusAsync(sessionId, capabilities.MaxPartitions, cancellationToken);
                await PullZoneStatusAsync(sessionId, effectiveZones, cancellationToken);

                _panelState.UpdateSession(sessionId, s => s.ConnectionPhase = ConnectionPhase.Connected);
                _panelState.OnConfigurationComplete(sessionId);
                _logger.LogInformation("Panel configuration pull complete");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Panel configuration pull cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during panel configuration pull");
            }
            finally
            {
                _panelState.UpdateSession(sessionId, s => s.IsInitialized = true);
                session.ConfigLock.Release();
            }
        }

        private SendSectionRead CreateSendDelegate(string sessionId) =>
            async (request, token) =>
            {
                var response = await _mediator.Send(new SessionCommand
                {
                    SessionID = sessionId,
                    MessageData = request
                }, token);
                return response.Success ? response.MessageData as SectionReadResponse : null;
            };

        /// <summary>
        /// Populates session-level PartitionState and ZoneState from the installer configuration.
        /// Only creates entries for enabled partitions and non-null zone definitions.
        /// </summary>
        private void ApplyConfigurationToSessionState(string sessionId, int effectiveZones, int maxPartitions)
        {
            var session = _panelState.GetSession(sessionId);
            var config = session?.Configuration;
            if (config is null) return;

            var partEnables = config.PartitionEnables.Values;
            var partLabels = config.PartitionLabels.Values;

            // Populate partition state — only enabled partitions
            for (int p = 0; p < maxPartitions; p++)
            {
                if (p < partEnables.Count && partEnables[p] != PanelConfiguration.Sections.PartitionEnable.Enabled)
                    continue;

                var partNum = (byte)(p + 1);
                var state = _panelState.GetPartition(sessionId, partNum)
                    ?? new PartitionState { PartitionNumber = partNum };

                if (p < partLabels.Count)
                {
                    var label = partLabels[p];
                    if (!string.IsNullOrEmpty(label))
                        state.Name = FormatLabelLine1(label);
                }

                _panelState.UpdatePartition(sessionId, state);
            }

            // Populate zone state — only non-null zone definitions
            var zoneDefs = config.ZoneDefinitions.Values;
            var zoneLabels = config.ZoneLabels.Values;
            var assignments = config.ZoneAssignments.Values;

            for (int z = 0; z < effectiveZones; z++)
            {
                if (z < zoneDefs.Count && zoneDefs[z] == PanelConfiguration.Sections.ZoneDefinition.NullZone)
                    continue;

                var zoneNum = (byte)(z + 1);
                var zone = _panelState.GetZone(sessionId, zoneNum)
                    ?? new ZoneState { ZoneNumber = zoneNum };

                // Labels
                if (z < zoneLabels.Count)
                {
                    var label = zoneLabels[z];
                    if (!string.IsNullOrEmpty(label))
                    {
                        zone.DisplayNameLine1 = label.Length >= 14 ? label[..14].TrimEnd() : label.TrimEnd();
                        zone.DisplayNameLine2 = label.Length > 14 ? label[14..Math.Min(28, label.Length)].TrimEnd() : null;
                    }
                }

                // Partition assignments from config
                zone.Partitions.Clear();
                for (int p = 0; p < maxPartitions; p++)
                {
                    if (p < assignments.GetLength(0) && z < assignments.GetLength(1) && assignments[p, z])
                        zone.Partitions.Add((byte)(p + 1));
                }

                _panelState.UpdateZone(sessionId, zone);
            }

            _logger.LogInformation(
                "Applied installer config to session state: {Zones} zones, {Partitions} partitions",
                effectiveZones, maxPartitions);
        }

        private static string FormatLabelLine1(string label) =>
            label.Length >= 14 ? label[..14].TrimEnd() : label.TrimEnd();

        private async Task PullZoneLabelsAsync(string sessionId, int maxZones, CancellationToken ct)
        {
            for (int start = 1; start <= maxZones; start += MaxLabelsPerRequest)
            {
                int end = Math.Min(start + MaxLabelsPerRequest - 1, maxZones);

                var response = await RequestAsync<NotificationLabelText>(
                    sessionId,
                    new CommandRequestMessage
                    {
                        Request = new NotificationLabelText { Collection = NotificationLabelText.LabelCollection.Zone, Start = start, End = end }
                    },
                    ct);

                if (response == null)
                {
                    _logger.LogWarning("Failed to get zone labels {Start}-{End}",
                        start, end);
                }
            }

            _logger.LogInformation("Pulled zone labels");
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
                        Request = new NotificationLabelText { Collection = NotificationLabelText.LabelCollection.Partition, Start = start, End = end }
                    },
                    ct);

                if (response == null)
                {
                    _logger.LogWarning("Failed to get partition labels {Start}-{End}",
                        start, end);
                }
            }

            _logger.LogInformation("Pulled partition labels");
        }

        private async Task PullPartitionStatusAsync(string sessionId, int maxPartitions, CancellationToken ct)
        {
            var config = _panelState.GetSession(sessionId)?.Configuration;
            var partEnables = config?.PartitionEnables.Values;

            for (int partition = 1; partition <= maxPartitions; partition++)
            {
                // Skip disabled partitions when config data is available
                if (partEnables is not null
                    && partition - 1 < partEnables.Count
                    && partEnables[partition - 1] != PanelConfiguration.Sections.PartitionEnable.Enabled)
                    continue;
                var response = await RequestAsync<ModulePartitionStatus>(
                    sessionId,
                    new CommandRequestMessage
                    {
                        Request = new ModulePartitionStatus { Partition = partition }
                    },
                    ct);

                if (response == null)
                {
                    _logger.LogWarning("Failed to get status for partition {Partition}",
                        partition);
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

                _logger.LogDebug("Partition {Partition} Status1=0x{S1:X} Status2=0x{S2:X} → {Status}, IsReady={IsReady}",
                    partitionNumber, (byte)s1, (byte)s2, state.Status, state.IsReady);
            }

            _logger.LogInformation("Pulled initial partition status");
        }

        private async Task PullZoneStatusAsync(string sessionId, int maxZones, CancellationToken ct)
        {
            var config = _panelState.GetSession(sessionId)?.Configuration;
            var zoneDefs = config?.ZoneDefinitions.Values;

            var response = await RequestAsync<ModuleZoneStatus>(
                sessionId,
                new CommandRequestMessage
                {
                    Request = new ModuleZoneStatus { ZoneStart = 1, ZoneCount = maxZones }
                },
                ct);

            if (response == null)
            {
                _logger.LogWarning("Failed to get zone status");
                return;
            }

            for (int i = 0; i < response.ZoneStatusBytes.Length; i++)
            {
                var zoneNumber = (byte)(response.ZoneStart + i);
                var zoneIndex = zoneNumber - 1;
                var status = response.ZoneStatusBytes[i];

                // Skip null zones when config data is available
                if (zoneDefs is not null
                    && zoneIndex < zoneDefs.Count
                    && zoneDefs[zoneIndex] == PanelConfiguration.Sections.ZoneDefinition.NullZone)
                    continue;

                var zone = _panelState.GetZone(sessionId, zoneNumber)
                    ?? new ZoneState { ZoneNumber = zoneNumber };

                zone.IsOpen = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Open);
                zone.IsFaulted = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Fault);
                zone.IsTampered = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Tamper);
                zone.IsBypassed = status.HasFlag(ModuleZoneStatus.ZoneStatusEnum.Bypass);
                zone.LastUpdated = DateTime.UtcNow;

                // Partition assignment: keep existing if already populated by config read
                if (!zone.Partitions.Any())
                    zone.Partitions.Add(1);

                _panelState.UpdateZone(sessionId, zone);
            }

            _logger.LogInformation("Pulled initial status for {Count} zones",
                response.ZoneStatusBytes.Length);
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
                _logger.LogWarning("Command {Command} failed: {Error}",
                    typeof(T).Name, response.ErrorMessage);
                return null;
            }

            return response.MessageData as T;
        }
    }
}
