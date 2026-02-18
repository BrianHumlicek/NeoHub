using System.Collections.Concurrent;
using DSC.TLink.ITv2.Messages;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR
{
    /// <summary>
    /// Pure shared state that correlates outbound app-sequence commands with their deferred inbound responses.
    /// Registered as a singleton using (sessionId, appSequence) composite keys.
    /// 
    /// Written to by AppSequencePipelineBehavior (outbound) via RegisterAsync.
    /// Read from by InboundNotificationPublisher (inbound) via TryComplete.
    /// </summary>
    internal class AppSequenceTransactionRegistry
    {
        private readonly ConcurrentDictionary<(string sessionId, byte appSequence), TaskCompletionSource<IMessageData>> _pending = new();
        private readonly ILogger<AppSequenceTransactionRegistry> _logger;

        public AppSequenceTransactionRegistry(ILogger<AppSequenceTransactionRegistry> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Register an app sequence to wait for a deferred response.
        /// Returns a task that completes when TryComplete matches this registration.
        /// </summary>
        public Task<IMessageData> RegisterAsync(string sessionId, byte appSequence, CancellationToken cancellationToken)
        {
            var key = (sessionId, appSequence);
            var tcs = new TaskCompletionSource<IMessageData>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_pending.TryAdd(key, tcs))
            {
                _logger.LogWarning("AppSequence {AppSequence} already registered for session {SessionId}, replacing", appSequence, sessionId);
                _pending[key] = tcs;
            }

            _logger.LogDebug("Registered AppSequence {AppSequence} for session {SessionId}", appSequence, sessionId);

            cancellationToken.Register(() =>
            {
                if (_pending.TryRemove(key, out var removed))
                {
                    removed.TrySetCanceled(cancellationToken);
                    _logger.LogDebug("AppSequence {AppSequence} registration cancelled for session {SessionId}", appSequence, sessionId);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Try to match a single message against pending registrations.
        /// Returns true if the message was consumed (should not be published as a notification).
        /// </summary>
        public bool TryComplete(string sessionId, IMessageData messageData)
        {
            if (messageData is IAppSequenceMessage appMsg && _pending.TryRemove((sessionId, appMsg.AppSequence), out var tcs))
            {
                _logger.LogDebug("AppSequence {AppSequence} matched by {MessageType} for session {SessionId}",
                    appMsg.AppSequence, messageData.GetType().Name, sessionId);
                tcs.TrySetResult(messageData);
                return true;
            }
            return false;
        }
    }
}
