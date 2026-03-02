using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using NeoHub.Services.Models;

namespace NeoHub.Services.Handlers
{
    /// <summary>
    /// Handles label text notifications — both solicited (responses to our requests)
    /// and unsolicited (panel-initiated updates). Updates zone or partition names
    /// based on the label type byte.
    /// </summary>
    public class LabelTextNotificationHandler
        : INotificationHandler<SessionNotification<NotificationLabelText>>
    {
        private const int ZoneLabelType = 0xD1;
        private const int PartitionLabelType = 0xD3;

        private readonly IPanelStateService _service;
        private readonly ILogger<LabelTextNotificationHandler> _logger;

        public LabelTextNotificationHandler(
            IPanelStateService service,
            ILogger<LabelTextNotificationHandler> logger)
        {
            _service = service;
            _logger = logger;
        }

        public Task Handle(
            SessionNotification<NotificationLabelText> notification,
            CancellationToken cancellationToken)
        {
            var msg = notification.MessageData;
            var sessionId = notification.SessionId;

            switch (msg.Unknown)
            {
                case ZoneLabelType:
                    ApplyZoneLabels(sessionId, msg);
                    break;
                case PartitionLabelType:
                    ApplyPartitionLabels(sessionId, msg);
                    break;
                default:
                    _logger.LogWarning(
                        "Unknown label type 0x{Type:X2} for session {SessionId}, Start={Start} End={End}",
                        msg.Unknown, sessionId, msg.Start, msg.End);
                    break;
            }

            return Task.CompletedTask;
        }

        private void ApplyZoneLabels(string sessionId, NotificationLabelText msg)
        {
            for (int i = 0; i < msg.Labels.Length; i++)
            {
                var zoneNumber = (byte)(msg.Start + i);
                var zone = _service.GetZone(sessionId, zoneNumber)
                    ?? new ZoneState { ZoneNumber = zoneNumber };

                zone.DisplayNameLine1 = msg.Labels[i].Substring(0, 14).Trim();
                zone.DisplayNameLine2 = msg.Labels[i].Substring(14, 14).Trim();

                if (string.IsNullOrEmpty(zone.DisplayNameLine1))
                    zone.DisplayNameLine1 = null;
                if (string.IsNullOrEmpty(zone.DisplayNameLine2))
                    zone.DisplayNameLine2 = null;

                _service.UpdateZone(sessionId, zone);
            }

            _logger.LogDebug(
                "Applied {Count} zone labels (Start={Start}) for session {SessionId}",
                msg.Labels.Length, msg.Start, sessionId);
        }

        private void ApplyPartitionLabels(string sessionId, NotificationLabelText msg)
        {
            int applied = 0;
            for (int i = 0; i < msg.Labels.Length; i++)
            {
                var partitionNumber = (byte)(msg.Start + i);
                var partition = _service.GetPartition(sessionId, partitionNumber);
                if (partition == null)
                    continue;

                partition.Name = msg.Labels[i].Trim();
                _service.UpdatePartition(sessionId, partition);
                applied++;
            }

            _logger.LogDebug(
                "Applied {Count} partition labels (Start={Start}) for session {SessionId}",
                applied, msg.Start, sessionId);
        }
    }
}
