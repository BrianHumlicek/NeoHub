using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR
{
    /// <summary>
    /// Unified mediator that handles both outbound commands (from Blazor UI) 
    /// and publishes inbound notifications (from panel).
    /// Registered as singleton - uses SessionManager for routing.
    /// </summary>
    internal class SessionMediator : IRequestHandler<SessionCommand, SessionResponse>
    {
        private readonly IMediator _mediator;
        private readonly IITv2SessionManager _sessionManager;
        private readonly ILogger<SessionMediator> _logger;

        public SessionMediator(
            IMediator mediator,
            IITv2SessionManager sessionManager,
            ILogger<SessionMediator> logger)
        {
            _mediator = mediator;
            _sessionManager = sessionManager;
            _logger = logger;
        }

        #region Command Handling (Outbound from Blazor UI)

        public async Task<SessionResponse> Handle(
            SessionCommand request,
            CancellationToken cancellationToken)
        {
            var session = _sessionManager.GetSession(request.SessionID);
            if (session == null)
            {
                _logger.LogWarning("Command failed - session {SessionId} not found", request.SessionID);
                return new SessionResponse
                {
                    Success = false,
                    ErrorMessage = $"Session {request.SessionID} not found"
                };
            }

            try
            {
                var result = await session.SendAsync(request.MessageData, cancellationToken);

                if (result.IsFailure)
                {
                    return new SessionResponse
                    {
                        Success = false,
                        ErrorMessage = result.Error?.ToString()
                    };
                }

                return new SessionResponse
                {
                    Success = true,
                    MessageData = result.Value,
                    ErrorDetail = result.Value switch
                    {
                        CommandResponse cmdResp => $"Command Response Code: {cmdResp.ResponseCode.Description()}",
                        _ => null
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling command for session {SessionId}", request.SessionID);
                return new SessionResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        #endregion

        #region Notification Publishing (Inbound from Panel)

        /// <summary>
        /// Publishes an inbound message as a typed SessionNotification&lt;T&gt;.
        /// Called by the connection handler for each notification from the session.
        /// MultipleMessagePacket expansion is already handled by the session.
        /// </summary>
        public async Task PublishNotificationAsync(string sessionId, IMessageData message)
        {
            try
            {
                var messageType = message.GetType();
                var notificationType = typeof(SessionNotification<>).MakeGenericType(messageType);

                var notification = Activator.CreateInstance(
                    notificationType, sessionId, message, DateTime.UtcNow);

                if (notification == null)
                {
                    _logger.LogError("Failed to create notification for type {MessageType}", messageType.Name);
                    return;
                }

                await _mediator.Publish(notification);

                _logger.LogTrace("Published SessionNotification<{MessageType}> for session {SessionId}",
                    messageType.Name, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing notification for session {SessionId}", sessionId);
            }
        }

        #endregion
    }
}
