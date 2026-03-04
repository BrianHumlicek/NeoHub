using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using NeoHub.Services.Models;
using static DSC.TLink.ITv2.Messages.NotificationPartitionReadyStatus;

namespace NeoHub.Services.Handlers
{
    /// <summary>
    /// Handles partition ready-status notifications and updates the state service.
    /// </summary>
    public class PartitionStatusNotificationHandler 
        : INotificationHandler<SessionNotification<NotificationPartitionReadyStatus>>
    {
        private readonly IPanelStateService _service;
        private readonly ILogger<PartitionStatusNotificationHandler> _logger;

        public PartitionStatusNotificationHandler(
            IPanelStateService service,
            ILogger<PartitionStatusNotificationHandler> logger)
        {
            _service = service;
            _logger = logger;
        }

        public Task Handle(
            SessionNotification<NotificationPartitionReadyStatus> notification,
            CancellationToken cancellationToken)
        {
            var msg = notification.MessageData;
            var sessionId = notification.SessionId;

            _logger.LogDebug("Partition {Partition} status: {Status}",
                msg.PartitionNumber, msg.Status);

            var partition = _service.GetPartition(sessionId, msg.PartitionNumber) 
                ?? new PartitionState { PartitionNumber = msg.PartitionNumber };
            
            partition.IsReady = msg.Status is PartitionReadyStatusEnum.ReadyToArm 
                                            or PartitionReadyStatusEnum.ReadyToForceArm;

            // Any ready-status notification means the partition is disarmed.
            // This clears stale armed/arming state if the panel cancelled arming
            // (e.g., door opened during exit delay).
            partition.Status = PartitionStatus.Disarmed;
            partition.ExitDelayActive = false;
            partition.ExitDelayStartedAt = null;
            partition.ExitDelayDurationSeconds = 0;
            partition.ExitDelayAudible = false;
            partition.ExitDelayUrgent = false;

            partition.LastUpdated = notification.ReceivedAt;

            _service.UpdatePartition(sessionId, partition);

            return Task.CompletedTask;
        }
    }
}