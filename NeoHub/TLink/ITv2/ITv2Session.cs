// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using DSC.TLink.Extensions;
using DSC.TLink.ITv2.Encryption;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using DSC.TLink.ITv2.Transactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Pipelines;
using System.Text;

namespace DSC.TLink.ITv2
{
    internal class ITv2Session : IDisposable
    {
        private readonly ILogger _log;
        private readonly TLinkClient _tlinkClient;
        private readonly ITv2Settings _itv2Settings;
        private readonly List<Transaction> _pendingTransactions = new();
        private readonly SemaphoreSlim _transactionSemaphore = new SemaphoreSlim(1, 1);
        private readonly TaskCompletionSource flushQueueTCS = new TaskCompletionSource();
        private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();

        private string? _sessionID;
        private byte _localSequence = 1, _appSequence;
        private byte _remoteSequence;
        private EncryptionHandler? _encryptionHandler;
        private sessionState _sessionState;

        public ITv2Session(
            TLinkClient tlinkClient,
            IOptions<ITv2Settings> settingsOptions, 
            ILogger<ITv2Session> logger)
        {
            _tlinkClient = tlinkClient ?? throw new ArgumentNullException(nameof(tlinkClient));
            _itv2Settings = settingsOptions.Value ?? throw new ArgumentNullException(nameof(settingsOptions));
            _log = logger ?? throw new ArgumentNullException(nameof(logger));
            _sessionState = sessionState.Uninitialized;
        }

        public string SessionID => _sessionID ?? throw new InvalidOperationException($"Session must be initialized to get property {nameof(SessionID)}");

        public async Task<bool> InitializeSession(IDuplexPipe transport, CancellationToken cancellationToken = default)
        {
            if (_sessionState != sessionState.Uninitialized)
                throw new InvalidOperationException("Session must be uninitialized.");
            try
            {
                _tlinkClient.InitializeTransport(transport);

                // Combine external token with internal shutdown token
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
                var linkedToken = linkedCts.Token;

                var clientResult = await WaitForClientResultAsync(linkedToken);

                //This ID is [851][422] Integration Identification Number and is part of every message.  I just take it from the first message.
                _sessionID = Encoding.UTF8.GetString(clientResult.Header);

                _log.LogInformation("Received connection from Integration ID {sessionID}", _sessionID);

                ITv2MessagePacket packet = ParseITv2Message(clientResult.Payload);

                await HandshakeAsync(packet, linkedToken);

            }
            catch (Exception ex)
            {
                _sessionState = sessionState.Closed;
                _log.LogError(ex, "Error initializing session");
                return false;
            }
            _log.LogInformation("Session {sessionID} initialized successfully", _sessionID);
            _sessionState = sessionState.Connected;
            return true;
        }

        private async Task HandshakeAsync(ITv2MessagePacket messagePacket, CancellationToken cancellation)
        {
            OpenSession openSessionMessage = messagePacket.messageData.As<OpenSession>();

            _log.LogInformation("Open Session message for encryption type {encryptionType}", openSessionMessage.EncryptionType);

            await executeInboundTransactionAsync(messagePacket);

            await executeOutboundTransactionAsync(openSessionMessage);

            SetEncryptionHandler(openSessionMessage.EncryptionType);

            messagePacket = await WaitForMessageAsync(cancellation);

            RequestAccess requestAccess = messagePacket.messageData.As<RequestAccess>();

            _log.LogInformation("Request Access message received");

            _encryptionHandler!.ConfigureOutboundEncryption(requestAccess.Initializer);

            try
            {
                await executeInboundTransactionAsync(messagePacket);

                requestAccess = new RequestAccess() { Initializer = _encryptionHandler.ConfigureInboundEncryption() };

                await executeOutboundTransactionAsync(requestAccess);
            }
            catch
            {
                _log.LogError("There was a problem negotiating encryption");
                if (openSessionMessage.EncryptionType == EncryptionType.Type1)
                {
                    _log.LogError("The remote communicator is configured for encryption type 1.  This means that the Type 1 Access Code [851][423,450,477,504] is most likely incorrect.");
                }
                if (openSessionMessage.EncryptionType == EncryptionType.Type2)
                {
                    _log.LogError("The remote communicator is configured for encryption type 2.  This means that the Type 2 Access Code [851][700,701,702,703] is most likely incorrect.");
                }
                throw;
            }
            
            return;
            
            /*Local methods*****************************************************/
            async Task executeInboundTransactionAsync(ITv2MessagePacket inboundMessagePacket)
            {
                EnsureInboundReceiverSequence(ref inboundMessagePacket);
                var transaction = CreateTransaction(inboundMessagePacket.messageData);
                await transaction.BeginInboundAsync(inboundMessagePacket, cancellation);
                await completeTransactionAsync(transaction);
            }
            async Task executeOutboundTransactionAsync(IMessageData messageData)
            {
                var messagePacket = CreateNextOutboundMessagePacket(messageData);
                var transaction = CreateTransaction(messageData);
                await transaction.BeginOutboundAsync(messagePacket, cancellation);
                await completeTransactionAsync(transaction);
            }
            async Task completeTransactionAsync(Transaction transaction)
            {
                ITv2MessagePacket messagePacket;
                while (transaction.CanContinue)
                {
                    messagePacket = await WaitForMessageAsync(cancellation);
                    if (!await transaction.TryContinueAsync(messagePacket, cancellation))
                    {
                        _log.LogDebug($"Unable to continue transaction at message {messagePacket.messageData.GetType()}", serializeMessagePacket(messagePacket));
                        throw new Exception($"Unable to continue handshake");
                    }
                }
            }
        }

        /// <summary>
        /// Listen for incoming messages and process them through transactions.
        /// </summary>
        /// <param name="cancellationToken">External cancellation token</param>
        public async Task ListenAsync(Action<Task<TransactionResult>> continuation, CancellationToken cancellationToken = default)
        {
            if (_sessionState != sessionState.Connected)
                throw new InvalidOperationException("Session is not connected/initialized");

            // Combine external token with internal shutdown token
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            var linkedToken = linkedCts.Token;

            //The remote communicator will queue messages if there is a disconnection and send them all
            //once the connection is re-established.  When these messages are being sent, the communicator
            //doesn't seem to respond/respect sequence numbers of messages it receives.  So, I use this
            //oneshot timer to allow the queue to empty before I allow messages to be sent out.
            Timer? flushQueueTimer = new Timer(_ => 
            {
                _log.LogInformation("Receive queue is flushed.  Ready to start sending");
                flushQueueTCS.SetResult();
                flushQueueTimer = null; //setting this is null combined with the elvis operator gives me quasi-polymorphism so I avoid something like an If check for every message..
                beginHeartBeat(linkedToken);
            });
            _log.LogInformation("ITv2 session started, listening for messages.");

            try
            {
                while (!linkedToken.IsCancellationRequested)
                {
                    flushQueueTimer?.Change(2000, Timeout.Infinite);    //I figure 2 seconds of no messages means the queue is flushed and we can start sending again.
                    var messagePacket = await WaitForMessageAsync(linkedToken);
                    flushQueueTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                    _remoteSequence = messagePacket.senderSequence;
                    await handleInboundMessagePacket(messagePacket, continuation, linkedToken);
                }
            }
            catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
            {
                _log.LogInformation("ITv2 session listen loop cancelled");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Fatal error in ITv2 session listen loop");
                throw;
            }
            finally
            {
                _sessionState = sessionState.Closed;
                _log.LogInformation("ITv2 session listen loop exited");
            }
        }
        async Task handleInboundMessagePacket(ITv2MessagePacket messagePacket, Action<Task<TransactionResult>> continuation, CancellationToken cancellation)
        {
            // Acquire lock with timeout to prevent deadlock
            if (!await _transactionSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellation))
            {
                _log.LogError("Transaction semaphore timeout - possible deadlock");
                throw new TimeoutException("Failed to acquire transaction lock within 30 seconds");
            }
            try
            {
                foreach (var waitingTransaction in _pendingTransactions)
                {
                    if (await waitingTransaction.TryContinueAsync(messagePacket, cancellation))
                    {
                        return;
                    }
                }

                _log.LogMessageDebug("Received", messagePacket.messageData);
                if (messagePacket.messageData is DefaultMessage defaultMessage)
                {
                    _log.LogWarning("Command {command}", defaultMessage.Command);
                    _log.LogWarning($"Data: {ILoggerExtensions.Enumerable2HexString(defaultMessage.Data)}");
                }

                EnsureInboundReceiverSequence(ref messagePacket);

                var newTransaction = CreateTransaction(messagePacket.messageData);
                
                _ = newTransaction.Result.ContinueWith(continuation, cancellation);

                _log.LogDebug("New {TransactionType} started: {MessageType}", newTransaction.GetType().Name, messagePacket.messageData.GetType().Name);
                await newTransaction.BeginInboundAsync(messagePacket, cancellation);

                if (newTransaction.CanContinue)
                {
                    _pendingTransactions.Add(newTransaction);
                }
                else
                {
                    newTransaction.Dispose();
                    _log.LogDebug("Transaction completed immediately: {MessageType}", messagePacket.messageData.GetType().Name);
                }

                var staleTransactions = _pendingTransactions.Where(tx => !tx.CanContinue).ToList();
                foreach (var stale in staleTransactions)
                {
                    _log.LogDebug("Removing completed transaction: {Type}", stale.GetType().Name);
                    _pendingTransactions.Remove(stale);
                    stale.Dispose();
                }
            }
            finally
            {
                _transactionSemaphore.Release();
            }
        }

        void beginHeartBeat(CancellationToken cancellation)
        {
            Task.Run(async () =>
            {
                try
                {
                    //await Task.Delay(10000);
                    ////await SendMessageAsync(new CommandRequestMessage() { CommandRequest = ITv2Command.Connection_Software_Version });
                    ////_log.LogDebug("Sent command request: SW Version");
                    ////await SendMessageAsync(new CommandRequestMessage() { CommandRequest = ITv2Command.ModuleStatus_Global_Status });
                    ////_log.LogDebug("Sent command request: Module global status ");
                    //await SendMessageAsync(new CommandRequestMessage() { CommandRequest = ITv2Command.ModuleStatus_Zone_Status, Data =  [0x01, 0x01, 0x01, 0x07 ] });
                    //_log.LogDebug("Sent command request: Module zone status");
                    do
                    {
                        //I have found that the connection times out at 2 minutes.
                        await Task.Delay(TimeSpan.FromSeconds(100), cancellation);
                        await SendMessageAsync(new ConnectionPoll(), cancellation);
                        _log.LogDebug("Sent Heartbeat");

                    } while (!cancellation.IsCancellationRequested);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error sending heartbeat");
                    
                }
            }, cancellation);

        }
        /// <summary>
        /// Send a message and manage its transaction lifecycle.
        /// </summary>
        public async Task<TransactionResult> SendMessageAsync(IMessageData messageData, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            var linkedToken = linkedCts.Token;
            await flushQueueTCS.Task;

            var newTransaction = CreateTransaction(messageData);

            if (!await _transactionSemaphore.WaitAsync(TimeSpan.FromSeconds(30), linkedToken).ConfigureAwait(false))
            {
                throw new TimeoutException("Failed to acquire transaction lock for send within 30 seconds");
            }

            try
            {
                var message = CreateNextOutboundMessagePacket(messageData);

                _log.LogMessageDebug("Sending", messageData);
                await newTransaction.BeginOutboundAsync(message, linkedToken).ConfigureAwait(false);
                
                if (newTransaction.CanContinue)
                {
                    _pendingTransactions.Add(newTransaction);
                    _log.LogDebug("Outbound transaction started: {MessageType}", messageData.GetType().Name);
                }
            }
            finally
            {
                _transactionSemaphore.Release();
            }
            //return await must happen outside of semaphore lock to prevent deadlocks.
            return await newTransaction.Result;
        }

        /// <summary>
        /// Immediately shutdown the session, cancelling all pending operations.
        /// </summary>
        public async Task ShutdownAsync()
        {
            if (_shutdownCts.IsCancellationRequested)
            {
                _log.LogWarning("Shutdown already initiated");
                return;
            }

            _log.LogInformation("Initiating ITv2 session shutdown");

            // Cancel all operations
            _shutdownCts.Cancel();

            // Wait for transaction lock and abort all transactions
            if (await _transactionSemaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                try
                {
                    var transactionCount = _pendingTransactions.Count;
                    foreach (var transaction in _pendingTransactions.ToArray())
                    {
                        try
                        {
                            transaction.Abort();
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Error aborting transaction during shutdown");
                        }
                    }
                    _pendingTransactions.Clear();
                    _log.LogInformation("Aborted {Count} pending transactions", transactionCount);
                }
                finally
                {
                    _transactionSemaphore.Release();
                }
            }
            else
            {
                _log.LogWarning("Could not acquire transaction lock during shutdown");
            }

            _log.LogInformation("ITv2 session shutdown complete");
        }

        async Task<ITv2MessagePacket> WaitForMessageAsync(CancellationToken cancellationToken)
        {
            var clientResult = await WaitForClientResultAsync(cancellationToken);
            return ParseITv2Message(clientResult.Payload);
        }

        async Task<TLinkClient.TLinkReadResult> WaitForClientResultAsync(CancellationToken cancellationToken)
        {
            var clientResult = await _tlinkClient.ReadMessageAsync(cancellationToken);

            if (clientResult.IsComplete)
            {
                _log.LogWarning("Client connection completed/closed");
                throw new TLinkPacketException(TLinkPacketException.Code.Disconnected);
            }

            return clientResult;
        }

        ITv2MessagePacket ParseITv2Message(byte[] payload)
        {
            // ITv2 Frame Structure:
            // [Length:1-2][Sender:1][Receiver:1][Command?:2][Payload:0-N][CRC:2]
            var decryptedPayload = _encryptionHandler?.HandleInboundData(payload) ?? payload;

            var messageBytes = new ReadOnlySpan<byte>(decryptedPayload);
            ITv2Framing.RemoveFraming(ref messageBytes); // Removes length prefix and validates CRC

            if (_log.IsEnabled(LogLevel.Trace))
            {
                _log.LogTrace("Received message (post decryption) {messageBytes}", messageBytes.ToArray());
            }


            // Sequence bytes track message ordering (wrap at 255)
            byte senderSeq = messageBytes.PopByte();      // Remote's incrementing counter
            byte receiverSeq = messageBytes.PopByte();    // Expected local sequence
            (byte? appSeq, IMessageData messageData) = MessageFactory.DeserializeMessage(messageBytes);

            return new ITv2MessagePacket(
                senderSequence:   senderSeq,
                receiverSequence: receiverSeq,
                appSequence:      appSeq,
                messageData:      messageData);
        }
        Transaction CreateTransaction(IMessageData messageData) => TransactionFactory.CreateTransaction(messageData, _log, SendTransactionMessageAsync);
        async Task SendTransactionMessageAsync(ITv2MessagePacket message, CancellationToken cancellationToken)
        {
            var messageBytes = serializeMessagePacket(message);
            _log.LogTrace("Sending message (pre encryption) {messageBytes}", messageBytes);
            ITv2Framing.AddFraming(messageBytes);
            var encryptedBytes = _encryptionHandler?.HandleOutboundData(messageBytes.ToArray()) ?? messageBytes.ToArray();
            await _tlinkClient.SendMessageAsync(encryptedBytes, cancellationToken);
        }
        List<byte> serializeMessagePacket(ITv2MessagePacket messagePacket)
        {
            var messageBytes = new List<byte>(
                [messagePacket.senderSequence,
                 messagePacket.receiverSequence,
                 ..messagePacket.messageData.Serialize(messagePacket.appSequence)
                ]);
            return messageBytes;
        }
        void EnsureInboundReceiverSequence(ref ITv2MessagePacket inboundMessagePacket)
        {
            if (inboundMessagePacket.appSequence.HasValue)
            {
                _appSequence = inboundMessagePacket.appSequence.Value;
            }
            if (inboundMessagePacket.receiverSequence != _localSequence)
            {
                inboundMessagePacket = inboundMessagePacket with { receiverSequence = (byte)_localSequence };
            }
        }
        ITv2MessagePacket CreateNextOutboundMessagePacket(IMessageData messageData)
        {
            return new ITv2MessagePacket(
                senderSequence: GetNextLocalSequence(),
                receiverSequence: _remoteSequence,
                appSequence: messageData.IsAppSequence ? GetNextAppSequence() : null,
                messageData: messageData);
        }
        byte GetNextLocalSequence() => ++_localSequence;
        byte GetNextAppSequence() => ++_appSequence;
        void SetEncryptionHandler(EncryptionType encryptionType)
        {
            if (_encryptionHandler is not null)
                throw new InvalidOperationException("Encryption handler has already been set.");

            _encryptionHandler = encryptionType switch
            {
                EncryptionType.Type1 => new Type1EncryptionHandler(_itv2Settings),
                EncryptionType.Type2 => new Type2EncryptionHandler(_itv2Settings),
                _ => throw new NotSupportedException($"Unsupported encryption type: {encryptionType}")
            };

            _log.LogInformation("Encryption handler set to {Type}", encryptionType);
        }
        public void Dispose()
        {
            _transactionSemaphore.Dispose();
            _shutdownCts.Dispose();
            _encryptionHandler?.Dispose();
        }
        enum sessionState
        {
            Uninitialized,
            Connected,
            Closed
        }
    }
}
