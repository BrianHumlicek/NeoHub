using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR
{
    /// <summary>
    /// MediatR pipeline behavior that intercepts outbound SessionCommands carrying an app sequence.
    /// 
    /// After the inner handler sends the command and gets the protocol-level ACK (CommandResponse),
    /// this behavior registers the app sequence with the tracker and awaits the real deferred
    /// response that will arrive later as an inbound notification from the panel.
    /// 
    /// Non-app-sequence commands pass through unmodified.
    /// </summary>
    internal class AppSequencePipelineBehavior : IPipelineBehavior<SessionCommand, SessionResponse>
    {
        private readonly AppSequenceTransactionRegistry _transactionRegistry;
        private readonly ILogger<AppSequencePipelineBehavior> _logger;

        public AppSequencePipelineBehavior(
            AppSequenceTransactionRegistry transactionRegistry,
            ILogger<AppSequencePipelineBehavior> logger)
        {
            _transactionRegistry = transactionRegistry;
            _logger = logger;
        }

        public async Task<SessionResponse> Handle(
            SessionCommand request,
            RequestHandlerDelegate<SessionResponse> next,
            CancellationToken cancellationToken)
        {
            var response = await next();

            if (!response.Success)
                return response;

            // The command's AppSequence property is assigned by ITv2Session.CreateNextOutboundMessagePacket
            // before SendMessageAsync returns, so request.MessageData reflects the assigned value.
            if (request.MessageData is IAppSequenceMessage appSeqCmd)
            {
                _logger.LogDebug("Awaiting deferred response for AppSequence {AppSequence} on session {SessionId}",
                    appSeqCmd.AppSequence, request.SessionID);

                var deferredResponse = await _transactionRegistry.RegisterAsync(
                    request.SessionID, appSeqCmd.AppSequence, cancellationToken);

                _logger.LogDebug("AppSequence {AppSequence} deferred response received: {MessageType}",
                    appSeqCmd.AppSequence, deferredResponse.GetType().Name);

                return new SessionResponse
                {
                    Success = true,
                    MessageData = deferredResponse
                };
            }

            return response;
        }
    }
}
