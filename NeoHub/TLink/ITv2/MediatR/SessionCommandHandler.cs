using DSC.TLink.Extensions;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR;

/// <summary>
/// Routes outbound commands to the correct session.
/// Replaces SessionMediator + AppSequencePipelineBehavior —
/// all protocol and app-sequence handling is now inside the session.
/// </summary>
internal class SessionCommandHandler : IRequestHandler<SessionCommand, Result<IMessageData>>
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

    public async Task<Result<IMessageData>> Handle(
        SessionCommand request, CancellationToken cancellationToken)
    {
        var session = _sessionManager.GetSession(request.SessionID);
        if (session is null)
        {
            _logger.LogWarning("Session {SessionId} not found", request.SessionID);
            return Result<IMessageData>.Fail(
                TLinkPacketException.Code.Unknown,
                $"Session '{request.SessionID}' not found");
        }

        return await session.SendAsync(request.MessageData, cancellationToken);
    }
}
