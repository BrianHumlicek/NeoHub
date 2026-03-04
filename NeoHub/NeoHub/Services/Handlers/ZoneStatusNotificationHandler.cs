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

            var zone = _service.GetZone(sessionId, msg.ZoneNumber) 
                ?? new ZoneState { ZoneNumber = msg.ZoneNumber };

            zone.IsOpen = msg.Status == NotificationLifestyleZoneStatus.LifeStyleZoneStatusCode.Open;
            zone.LastUpdated = notification.ReceivedAt;

            // TODO: No protocol-level way to get zone-partition mapping yet.
            // For now, assign all zones to partition 1.
            if (!zone.Partitions.Any())
                zone.Partitions.Add(1);

            _logger.LogDebug(
                "Zone {Zone} is now {Status} (Associated Partitions: {Partitions})",
                msg.ZoneNumber, zone.IsOpen ? "OPEN" : "CLOSED", string.Join(",", zone.Partitions));

            _service.UpdateZone(sessionId, zone);

            return Task.CompletedTask;
        }
    }
}