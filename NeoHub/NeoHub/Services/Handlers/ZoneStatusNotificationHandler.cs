using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using NeoHub.Services.Models;

namespace NeoHub.Services.Handlers
{
    /// <summary>
    /// Handles lifestyle zone status notifications (real-time open/close events).
    /// </summary>
    public class ZoneStatusNotificationHandler 
        : INotificationHandler<SessionNotification<NotificationLifestyleZoneStatus>>
    {
        private readonly IPanelStateService _service;
        private readonly ILogger<ZoneStatusNotificationHandler> _logger;

        public ZoneStatusNotificationHandler(
            IPanelStateService service,
            ILogger<ZoneStatusNotificationHandler> logger)
        {
            _service = service;
            _logger = logger;
        }

        public Task Handle(
            SessionNotification<NotificationLifestyleZoneStatus> notification,
            CancellationToken cancellationToken)
        {
            var msg = notification.MessageData;
            var sessionId = notification.SessionId;

            // Calculate which partition(s) this zone belongs to (zones 1-64 = partition 1, etc.)
            byte calculatedPartition = (byte)Math.Max(1, (msg.ZoneNumber - 1) / 64 + 1);

            var zone = _service.GetZone(sessionId, msg.ZoneNumber) 
                ?? new ZoneState 
                { 
                    ZoneNumber = msg.ZoneNumber,
                    Partitions = new List<byte> { calculatedPartition } // Default association
                };

            zone.IsOpen = msg.Status == NotificationLifestyleZoneStatus.LifeStyleZoneStatusCode.Open;
            zone.LastUpdated = notification.ReceivedAt;

            _logger.LogDebug(
                "Zone {Zone} is now {Status} (Associated Partitions: {Partitions})",
                msg.ZoneNumber, zone.IsOpen ? "OPEN" : "CLOSED", string.Join(",", zone.Partitions));

            _service.UpdateZone(sessionId, zone);

            return Task.CompletedTask;
        }
    }
}