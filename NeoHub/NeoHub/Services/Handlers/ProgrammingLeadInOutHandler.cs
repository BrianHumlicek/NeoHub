using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;

namespace NeoHub.Services.Handlers
{
    /// <summary>
    /// Tracks programming mode state from ProgrammingLeadInOut notifications.
    /// The panel sends LeadIn when installer programming begins and LeadOut when it ends.
    /// </summary>
    public class ProgrammingLeadInOutHandler
        : INotificationHandler<SessionNotification<ProgrammingLeadInOut>>
    {
        private readonly IPanelStateService _panelState;
        private readonly ILogger<ProgrammingLeadInOutHandler> _logger;

        public ProgrammingLeadInOutHandler(
            IPanelStateService panelState,
            ILogger<ProgrammingLeadInOutHandler> logger)
        {
            _panelState = panelState;
            _logger = logger;
        }

        public Task Handle(
            SessionNotification<ProgrammingLeadInOut> notification,
            CancellationToken cancellationToken)
        {
            var msg = notification.MessageData;
            var isLeadIn = msg.Programming == ProgrammingLeadInOut.ProgrammingType.LeadIn;

            _panelState.UpdateSession(notification.SessionId, session =>
            {
                session.IsInProgrammingMode = isLeadIn;
            });

            _logger.LogInformation(
                "Programming {Direction}: Partition={Partition}, Mode={Mode}, Access={Access}",
                isLeadIn ? "LeadIn" : "LeadOut",
                msg.Partition,
                msg.Mode,
                msg.Access);

            return Task.CompletedTask;
        }
    }
}
