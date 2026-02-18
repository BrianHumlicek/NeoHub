using DSC.TLink.ITv2.Messages;
using Microsoft.Extensions.Logging;
using static DSC.TLink.ITv2.ITv2Session;

namespace DSC.TLink.ITv2.Transactions
{
    /// <summary>
    /// Base class for all (except handshake) ITv2 protocol transactions.
    /// 
    /// Transaction Lifecycle:
    /// 1. **Creation**: TransactionFactory.CreateTransaction() based on message type
    /// 2. **Begin**: BeginInboundAsync() or BeginOutboundAsync() starts the transaction
    /// 3. **Continue**: ContinueAsync() handles subsequent correlated messages
    /// 4. **Completion**: Complete() or Abort() ends the transaction
    /// 
    /// Thread Safety:
    /// - All transactions run under ITv2Session._transactionSemaphore
    /// - Only one transaction processes at a time
    /// - Transactions can access session's private state directly
    /// 
    /// Timeout:
    /// - Each transaction has a configurable timeout (default 30s)
    /// - OnTimeout() called if transaction doesn't complete in time
    /// - Session periodically cleans up timed-out transactions
    /// </summary>
    internal abstract class Transaction : IDisposable
	{
        private ILogger _log;
        private Func<ITv2MessagePacket, CancellationToken, Task> _sendMessageDelegate;

        private byte localSequence, remoteSequence;
        //private byte? appSequence;
        private IMessageData? _initiatingMessage;
        private Func<ITv2MessagePacket, bool> isCorrelated = new Func<ITv2MessagePacket, bool>(message => false);

        // Timeout infrastructure
        private readonly TimeSpan _timeout;
		private readonly CancellationTokenSource _timeoutCts = new();

        private TaskCompletionSource<TransactionResult> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected Transaction(ILogger log, Func<ITv2MessagePacket, CancellationToken, Task> sendMessageDelegate, TimeSpan? timeout = null)
		{
            _log = log;
            _sendMessageDelegate = sendMessageDelegate;
            _timeout = timeout ?? Timeout.InfiniteTimeSpan;
        }
        public async Task BeginInboundAsync(ITv2MessagePacket message, CancellationToken cancellationToken)
        {
            _timeoutCts.CancelAfter(_timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token);

            remoteSequence = message.senderSequence;
            localSequence = message.receiverSequence;
            //appSequence = message.appSequence;
            isCorrelated = inboundCorrelataion;
            _initiatingMessage = message.messageData;
            await InitializeInboundAsync(linkedCts.Token);
        }

        public async Task BeginOutboundAsync(ITv2MessagePacket message, CancellationToken cancellationToken)
        {
            _timeoutCts.CancelAfter(_timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token);

            localSequence = message.senderSequence;
            remoteSequence = message.receiverSequence;
            //appSequence = message.appSequence;
            isCorrelated = outboundCorrelataion;
            _initiatingMessage = message.messageData;
            await _sendMessageDelegate(message, linkedCts.Token);
            await InitializeOutboundAsync(linkedCts.Token);
        }

        public async Task<bool> TryContinueAsync(ITv2MessagePacket message, CancellationToken cancellationToken)
        {
            if (isCorrelated.Invoke(message) && CanContinue)
            {
                //When sending an outbound transaction, I sync the remote sequence on the first (and subsequent) reply.
                //This mimicks the behaviour I see from the TL280, though it doesn't seem to impact functionality.
                remoteSequence = message.senderSequence;
                return await TryProcessMessageAsync(message, cancellationToken);
            }
            return false;
        }
        public bool CanContinue => Pending && !_timeoutCts.IsCancellationRequested;
        public Task<TransactionResult> Result => _completionSource.Task;
        protected ILogger log => _log;
        protected IMessageData InitiatingMessage => _initiatingMessage ?? throw new InvalidOperationException($"Cannot access {nameof(InitiatingMessage)} before sending or receiving");
        protected Task SendMessageAsync(IMessageData messageData, CancellationToken cancellationToken)
        {
            // Link with timeout cancellation
            //if (messageData is IAppSequenceMessage appSequencesMessage)
            //{
            //    appSequenceMessage.AppSequence = appSequence ?? throw new InvalidOperationException("AppSequence is not set for this transaction");
            //}
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutCts.Token);
            var message = new ITv2MessagePacket(localSequence, remoteSequence, messageData);
            return _sendMessageDelegate(message, linkedCts.Token);
        }
        protected abstract Task<bool> TryProcessMessageAsync(ITv2MessagePacket message, CancellationToken cancellationToken);
		protected abstract bool Pending { get; }
        protected abstract Task InitializeInboundAsync(CancellationToken cancellationToken);
		protected abstract Task InitializeOutboundAsync(CancellationToken cancellationToken);
        protected void SetResult(TransactionResult result)
        {
            result.CommandMessage = InitiatingMessage;
            _completionSource.TrySetResult(result);
        }
        bool inboundCorrelataion(ITv2MessagePacket message) => message.senderSequence == remoteSequence;
        bool outboundCorrelataion(ITv2MessagePacket message) => message.receiverSequence == localSequence;

        protected bool HandleUnexpectedResponse(ITv2MessagePacket message)
        {
            log.LogWarning("Received unexpected message during {TransactionType}: {MessageType}", GetType().Name, message.messageData.GetType().Name);
            Abort("Unexpected response");

            return message.messageData switch
            {
                SimpleAck => true,
                CommandResponse => true,
                _ => false
            };
        }
        /// <summary>
        /// Abort the transaction
        /// </summary>
        public void Abort(string resultMessage = "Transaction aborted")
        {
            log.LogWarning("{TransactionType} aborted", GetType().Name);
			_completionSource.SetResult(new TransactionResult(resultMessage));
            _timeoutCts.Cancel();
        }
        public void Dispose()
        {
            _timeoutCts.Dispose();            
        }
    }
}