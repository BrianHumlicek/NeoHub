using DSC.TLink.ITv2.Encryption;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DSC.TLink.ITv2
{
    internal partial class ITv2Session
    {
        private class HandshakeHandler : IRequestHandler<SessionRequest<OpenSession>, Result<SessionRequestResult>>,
                                         IRequestHandler<SessionRequest<RequestAccess>, Result<SessionRequestResult>>
        {
            private readonly ILogger<ITv2Session> _logger;
            private readonly ITv2Settings _settings;

            public HandshakeHandler(ILogger<ITv2Session> logger, ITv2Settings settings)
            {
                _logger = logger;
                _settings = settings;
            }

            //Step 1: Receive and acknowledge the OpenSession command from the panel.
            public async Task<Result<SessionRequestResult>> Handle(SessionRequest<OpenSession> request, CancellationToken cancellationToken)
            {
                var session = request.Session;
                session._sessionId = Encoding.UTF8.GetString(session._transport.DefaultHeader.Span);

                _logger.LogInformation("Received OpenSession command with encryption {EncryptionType} for session {SessionID}",
                    request.MessageData.EncryptionType, session.SessionId);

                if (session._state != SessionState.WaitingForOpenSession)
                    throw new InvalidOperationException($"Invalid session state for OpenSession: {session._state}");

                return new SessionRequestResult() { Continuation = (ct) => OpenSessionContinuation(request.MessageData, session, ct) };
            }

            //Step 2: Send an OpenSession command to the panel, and set the encryption handler.
            //I just send a copy of what was sent to me, but presumably  I could modify it if needed (e.g. to adjust buffer sizes or other parameters, but not the encryption type).
            private async Task<Result> OpenSessionContinuation(OpenSession openSessionMessage, ITv2Session session, CancellationToken cancellation)
            {
                try
                {
                    _logger.LogDebug("Executing OpenSession continuation for session {SessionID}", session.SessionId);
                    var result = await session.SendAsync(openSessionMessage, cancellation);

                    if (result.IsSuccess)
                    {
                        session._encryptionHandler = openSessionMessage.EncryptionType switch
                        {
                            EncryptionType.Type1 => new Type1EncryptionHandler(_settings),
                            EncryptionType.Type2 => new Type2EncryptionHandler(_settings),
                            _ => throw new InvalidOperationException($"Unsupported encryption type {openSessionMessage.EncryptionType}")
                        };

                        session._state = SessionState.WaitingForRequestAccess;
                    }
                    return result.ToResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send OpenSession reply for session {SessionID}", session.SessionId);
                    return Result.Fail(new TLinkError());
                }

            }

            //Step 3: Receive the RequestAccess command from the panel, which includes the initializer for the encryption.  Configure outbound encryption from the initializer.
            public async Task<Result<SessionRequestResult>> Handle(SessionRequest<RequestAccess> request, CancellationToken cancellation)
            {
                var session = request.Session;

                _logger.LogInformation("Received RequestAccess command for session {SessionID}", session.SessionId);

                if (session._state != SessionState.WaitingForRequestAccess)
                    throw new InvalidOperationException($"Invalid session state for RequestAccess: {session._state}");

                session._encryptionHandler!.ConfigureOutboundEncryption(request.MessageData.Initializer);

                return new SessionRequestResult() { Continuation = (ct) => RequestAccessContinuation(session, ct) };
            }

            //Step 4: Send a RequestAccess command to the panel.  Configure inbound encryption and send the initializer.
            private async Task<Result> RequestAccessContinuation(ITv2Session session, CancellationToken cancellation)
            {
                try
                {
                    _logger.LogDebug("Executing RequestAccess continuation for session {SessionID}", session.SessionId);
                    var requestAccess = new RequestAccess
                    {
                        Initializer = session._encryptionHandler!.ConfigureInboundEncryption()
                    };
                    var result = await session.SendAsync(requestAccess, cancellation);
                    if (result.IsSuccess)
                    {
                        _logger.LogInformation("Session {SessionID} is now connected and encrypted", session.SessionId);
                        session._state = SessionState.Connected;
                    }
                    return result.ToResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send RequestAccess reply for session {SessionID}", session.SessionId);
                    return Result.Fail(new TLinkError());
                }
            }
        }
    }
}