using DSC.TLink.ITv2.Messages;
using DSC.TLink.ITv2.Transactions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR
{
    /// <summary>
    /// Publishes inbound panel messages as MediatR notifications.
    /// 
    /// Responsibilities:
    /// - Awaits transaction results from ITv2Session
    /// - Unpacks MultipleMessagePacket into individual messages
    /// - Filters out messages claimed by AppSequenceResponseTracker
    /// - Publishes remaining messages as typed SessionNotification&lt;T&gt;
    /// </summary>
    internal class InboundNotificationPublisher
    {
        private readonly IMediator _mediator;
        private readonly AppSequenceTransactionRegistry _transactionRegistry;
        private readonly ILogger<InboundNotificationPublisher> _logger;

        public InboundNotificationPublisher(
            IMediator mediator,
            AppSequenceTransactionRegistry transactionRegistry,
            ILogger<InboundNotificationPublisher> logger)
        {
            _mediator = mediator;
            _transactionRegistry = transactionRegistry;
            _logger = logger;
        }

        /// <summary>
        /// Process an inbound transaction result: unpack, filter, and publish as notifications.
        /// Called by TLinkConnectionHandler for each message received from the panel.
        /// </summary>
        public async void Publish(string sessionId, Task<TransactionResult> transactionResultTask)
        {
            try
            {
                var result = await transactionResultTask;

                if (!result.Success)
                {
                    _logger.LogWarning("Transaction failed for session {SessionId}: {Error}",
                        sessionId, result.ErrorMessage);
                    return;
                }

                foreach (var messageData in UnpackMessages(result.ResponseMessage))
                {
                    if (_transactionRegistry.TryComplete(sessionId, messageData))
                        continue;

                    await PublishGenericNotification(sessionId, messageData);
                    _logger.LogTrace("Published SessionNotification<{MessageType}> for session {SessionId}",
                        messageData.GetType().Name, sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing notification for session {SessionId}", sessionId);
            }
        }

        private static IEnumerable<IMessageData> UnpackMessages(IMessageData messageData)
        {
            if (messageData is MultipleMessagePacket multiMessage)
                return multiMessage.Messages;

            return [messageData];
        }

        private async Task PublishGenericNotification(string sessionId, IMessageData messageData)
        {
            var messageType = messageData.GetType();
            var notificationType = typeof(SessionNotification<>).MakeGenericType(messageType);

            var notification = Activator.CreateInstance(
                notificationType,
                sessionId,
                messageData,
                DateTime.UtcNow);

            if (notification == null)
            {
                _logger.LogError("Failed to create notification for type {MessageType}", messageType.Name);
                return;
            }

            await _mediator.Publish(notification);
        }
    }
}
