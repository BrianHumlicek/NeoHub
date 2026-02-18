using DSC.TLink.Extensions;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using DSC.TLink.ITv2.Transactions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR
{
    /// <summary>
    /// Handles outbound commands from Blazor UI by routing to the appropriate ITv2 session.
    /// 
    /// App sequence correlation is handled transparently by AppSequencePipelineBehavior
    /// which wraps this handler in the MediatR pipeline.
    /// </summary>
    internal class SessionCommandHandler : IRequestHandler<SessionCommand, SessionResponse>
    {
        private readonly IITv2SessionManager _sessionManager;
        private readonly ILogger<SessionCommandHandler> _logger;

        public SessionCommandHandler(
            IITv2SessionManager sessionManager,
            ILogger<SessionCommandHandler> logger)
        {
            _sessionManager = sessionManager;
            _logger = logger;
        }

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
                var result = await session.SendMessageAsync(request.MessageData, cancellationToken);
                return new SessionResponse
                {
                    Success = result.Success,
                    MessageData = result.ResponseMessage,
                    ErrorMessage = result.ErrorMessage,
                    ErrorDetail = result.ResponseMessage switch
                    {
                        CommandResponse cmdresp => $"Command Response Code: {cmdresp.ResponseCode.Description()}",
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
    }
}
