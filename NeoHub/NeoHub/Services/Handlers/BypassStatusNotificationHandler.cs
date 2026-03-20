using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using NeoHub.Services.Models;

namespace NeoHub.Services.Handlers
{
    /// <summary>
    /// Handles single zone bypass status notifications (real-time bypass/unbypass events).
    /// Updates the zone's IsBypassed state so the UI reflects changes made from any source
    /// (keypad, ITv2, DLS).
    /// </summary>
    public class BypassStatusNotificationHandler
        : INotificationHandler<SessionNotification<SingleZoneBypassStatus>>
    {
        private readonly IPanelStateService _service;
        private readonly ILogger<BypassStatusNotificationHandler> _logger;

        public BypassStatusNotificationHandler(
            IPanelStateService service,
            ILogger<BypassStatusNotificationHandler> logger)
        {
            _service = service;
            _logger = logger;
        }

        public Task Handle(
            SessionNotification<SingleZoneBypassStatus> notification,
            CancellationToken cancellationToken)
        {
            var msg = notification.MessageData;
            var sessionId = notification.SessionId;

            var zone = _service.GetZone(sessionId, msg.ZoneNumber)
                ?? new ZoneState { ZoneNumber = msg.ZoneNumber };

            zone.IsBypassed = msg.BypassStatus == BypassStatusEnum.Bypassed;
            zone.LastUpdated = notification.ReceivedAt;

            _logger.LogDebug(
                "Zone {Zone} bypass status: {Status}",
                msg.ZoneNumber, msg.BypassStatus);

            _service.UpdateZone(sessionId, zone);

            return Task.CompletedTask;
        }
    }
}
