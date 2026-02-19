using DSC.TLink.ITv2.Encryption;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.Extensions;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using DSC.TLink.ITv2.Transactions;

namespace DSC.TLink.ITv2
{
    internal partial class ITv2Session : IITv2Session
    {
        private readonly ITLinkTransport _transport;
        private readonly ITv2Settings _settings;
        private readonly ILogger<ITv2Session> _logger;
        private readonly IMediator _mediator;

        private SessionState _state = SessionState.WaitingForOpenSession;
        private EncryptionHandler? _encryptionHandler;
        private string _sessionId = null!;

        private byte _localSequence = 1;
        private byte _remoteSequence;
        private byte _commandSequence;

        private IAsyncEnumerator<Result<TLinkMessage>>? _reader;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly CancellationTokenSource _shutdownCts = new();

        private readonly ConcurrentDictionary<byte, PendingCommand> _pendingCommands = new();
        private readonly ConcurrentQueue<IMessageData> _notifications = new();

        private ITv2Session(ITLinkTransport transport, ITv2Settings settings, ILogger<ITv2Session> logger, IMediator mediator)
        {
            _transport = transport;
            _settings = settings;
            _logger = logger;
            _mediator = mediator;
        }

        public string SessionId => _sessionId;

        internal static async Task<Result<ITv2Session>> CreateAsync(
            ITLinkTransport transport, 
            ITv2Settings settings, 
            ILogger<ITv2Session> logger,
            IMediator mediator,
            CancellationToken ct)
        {
            var session = new ITv2Session(transport, settings, logger, mediator);

            try
            {
                session._reader = transport.ReadAllAsync(ct).GetAsyncEnumerator(ct);
                //Minimal message pump that is enough to move opensession and requestaccess to their respective handlers.
                //we manually enumerate here so we can continue enumerating in the main GetNotificationsAsync loop after
                //the session is established, without needing to worry about concurrency on the reader or messages getting lost between the two loops.
                while (await session._reader.MoveNextAsync())
                {
                    var result = session.GetCurrentPacket();

                    var packet = result.Value;

                    await session.TryHandleCommandPacketAsync(packet, ct);
                    if (session._state == SessionState.Connected)
                    {
                        return session;
                    }
                }
                return Result<ITv2Session>.Fail(TLinkPacketException.Code.Disconnected, "Transport closed");
            }
            catch (Exception ex)
            {
                await session.DisposeAsync();
                logger.LogError(ex, "Failed to create session");
                return Result<ITv2Session>.Fail(TLinkPacketException.Code.Unknown, ex.Message);
            }
        }

        public async Task<Result<IMessageData>> SendAsync(IMessageData message, CancellationToken ct = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
            var token = linked.Token;

            await _sendLock.WaitAsync(token);
            PendingCommand? pendingCommand = null;

            try
            {
                var isCommandSequence = message is ICommandMessageData;

                if (isCommandSequence)
                {
                    var cmdSeq = ++_commandSequence;
                    ((ICommandMessageData)message).CorrelationID = cmdSeq;
                    pendingCommand = new PendingCommand();
                    _pendingCommands[cmdSeq] = pendingCommand;
                }

                var packet = new ITv2Packet(
                    SenderSequence: ++_localSequence,
                    ReceiverSequence: _remoteSequence,
                    Message: message);

                var sendResult = await SendPacketAsync(packet, token);
                if (sendResult.IsFailure)
                {
                    if (isCommandSequence && message is ICommandMessageData app)
                        _pendingCommands.TryRemove(app.CorrelationID, out _);
                    return Result<IMessageData>.Fail(sendResult.Error!.Value);
                }

                _logger.LogDebug("Sent {MessageType}", message.GetType().Name);
            }
            finally
            {
                _sendLock.Release();
            }

            if (pendingCommand is null)
            {
                return Result<IMessageData>.Ok(new SimpleAck());
            }

            try
            {
                var response = await pendingCommand.Response.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
                _logger.LogDebug("Command sequence response received: {Type}", response.GetType().Name);
                return Result<IMessageData>.Ok(response);
            }
            catch (TimeoutException)
            {
                if (message is ICommandMessageData app)
                    _pendingCommands.TryRemove(app.CorrelationID, out _);
                return Result<IMessageData>.Fail(TLinkPacketException.Code.Timeout, "No response within 60s");
            }
        }

        public async IAsyncEnumerable<IMessageData> GetNotificationsAsync([EnumeratorCancellation] CancellationToken cancellation = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdownCts.Token);
            var token = linked.Token;

            _logger.LogInformation("Notification stream started for session {SessionId}", _sessionId);

            while (!token.IsCancellationRequested && _reader is not null && await _reader.MoveNextAsync())
            {
                var result = GetCurrentPacket();

                var packet = result.Value;

                if (await TryHandleCommandPacketAsync(packet, token))
                {
                    continue;
                }
                //Else message is a notification.
                //All notifications get a simpleack response.
                await SendSimpleAckAsync(packet.SenderSequence, token);
                
                if (packet.Message is MultipleMessagePacket multi)
                {
                    foreach (var subMessage in multi.Messages)
                    {
                        if (subMessage is ICommandMessageData command)
                        {
                            await HandleCommandMessageAsync(command, token);
                        }
                        else
                        {
                            yield return subMessage;
                        }
                    }
                }
                else
                {
                    yield return packet.Message;
                }
            }
            _logger.LogInformation("Notification stream ended for session {SessionId}", _sessionId);
        }
        private Result<ITv2Packet> GetCurrentPacket()
        {
            var tlinkResult = _reader!.Current;
            if (tlinkResult.IsFailure)
            {
                _logger.LogWarning("Bad TLink frame: {Error}", tlinkResult.Error);
            }

            var parseResult = ParseTransportPayload(tlinkResult.Value.Payload);
            if (parseResult.IsFailure)
            {
                _logger.LogWarning("Bad ITv2 packet: {Error}", parseResult.Error);
            }

            var packet = parseResult.Value;
            _remoteSequence = packet.SenderSequence;
            return packet;
        }
        /// <summary>
        /// This is intended to handle any situation where we may not automatically send a SimpleAck response to a received message.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        private async Task<bool> TryHandleCommandPacketAsync(ITv2Packet packet, CancellationToken cancellation)
        {
            if (packet.Message is SimpleAck)
            {
                //Handler correlated ack
                return true;
            }
            else if (packet.Message is ICommandMessageData command)
            {
                await HandleCommandMessageAsync(command, cancellation);
                return true;
            }
            return false;
        }
        private async Task HandleCommandMessageAsync(ICommandMessageData command, CancellationToken cancellation)
        {
            if (_pendingCommands.TryRemove(command.CorrelationID, out var pending))
            {
                await SendSimpleAckAsync(_remoteSequence, cancellation);
                pending.Response.SetResult(command);
            }
            else
            {
                //So far the only inbound commands I'm aware of are the handshake commands, opensession and requestaccess.
                await InboundCommandHandlerAsync(command, cancellation);
            }
        }
        /// <summary>
        /// This routes the command message to an appropiate handler for execution.  
        /// </summary>
        /// <param name="command"></param>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task InboundCommandHandlerAsync(ICommandMessageData command, CancellationToken cancellation)
        {
            bool asyncCommand = command is CommandMessageData cmdData && cmdData.IsAsynchronous;

            if (asyncCommand)
            {
                await SendSimpleAckAsync(_remoteSequence, cancellation);
            }

            try
            {
                var requestType = typeof(SessionRequest<>).MakeGenericType(command.GetType());
                var request = Activator.CreateInstance(requestType);

                var sessionProp = requestType.GetProperty("Session");
                var messageDataProp = requestType.GetProperty("MessageData");

                sessionProp?.SetValue(request, this);
                messageDataProp?.SetValue(request, command);

                var handlerResponse = await _mediator.Send(request!, cancellation);

                var handlerResult = handlerResponse switch
                {
                    Result<SessionRequestResult> r => r,
                    _ => throw new InvalidOperationException()
                };

                IMessageData responseMessage = handlerResult.IsSuccess ? new CommandResponse { CorrelationID = command.CorrelationID, ResponseCode = CommandResponseCode.Success }
                                                                       : new CommandError { Command = MessageFactory.GetCommand(command), NackCode = ITv2NackCode.UnknownError };
                
                await SendAsync(responseMessage, cancellation);

                if (handlerResult.IsFailure)
                    return;

                if (handlerResult.Value.Continuation is null)
                    return;

                await handlerResult.Value.Continuation(cancellation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling inbound command {Type}", command.GetType().Name);
            }
        }
        private async Task SendSimpleAckAsync(byte senderSequence, CancellationToken ct)
        {
            await _sendLock.WaitAsync(ct);
            try
            {
                var ackPacket = new ITv2Packet(
                    SenderSequence: ++_localSequence,
                    ReceiverSequence: senderSequence,
                    Message: new SimpleAck());

                await SendPacketAsync(ackPacket, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<Result> SendPacketAsync(ITv2Packet packet, CancellationToken ct)
        {
            var bytes = SerializePacket(packet);
            ITv2Framing.AddFraming(bytes);

            var encrypted = _encryptionHandler?.HandleOutboundData(bytes.ToArray()) ?? bytes.ToArray();

            var result = await _transport.SendAsync(encrypted, ct);
            if (result.IsFailure)
                return Result.Fail(TLinkPacketException.Code.Disconnected, result.Error!.Value.Message);

            return Result.Ok();
        }

        private Result<ITv2Packet> ParseTransportPayload(ReadOnlyMemory<byte> rawPayload)
        {
            try
            {
                var payload = _encryptionHandler?.HandleInboundData(rawPayload.ToArray()) ?? rawPayload.ToArray();
                var span = new ReadOnlySpan<byte>(payload);

                var framingResult = ITv2Framing.RemoveFraming(ref span);
                if (framingResult.IsFailure)
                    return Result<ITv2Packet>.Fail(framingResult.Error!.Value);

                byte senderSeq = span.PopByte();
                byte receiverSeq = span.PopByte();
                var message = MessageFactory.DeserializeMessage(span);

                return Result<ITv2Packet>.Ok(new ITv2Packet(senderSeq, receiverSeq, message));
            }
            catch (Exception ex)
            {
                return Result<ITv2Packet>.Fail(TLinkPacketException.Code.Unknown, ex.Message);
            }
        }

        private static List<byte> SerializePacket(ITv2Packet packet)
        {
            var bytes = new List<byte>
            {
                packet.SenderSequence,
                packet.ReceiverSequence
            };
            bytes.AddRange(MessageFactory.SerializeMessage(packet.Message));
            return bytes;
        }

        public async ValueTask DisposeAsync()
        {
            _shutdownCts.Cancel();

            foreach (var (_, pending) in _pendingCommands)
                pending.Response.TrySetCanceled();
            _pendingCommands.Clear();

            if (_reader is not null)
                await _reader.DisposeAsync();

            _encryptionHandler?.Dispose();
            _sendLock.Dispose();
            _shutdownCts.Dispose();
        }

        private enum SessionState
        {
            WaitingForOpenSession,
            WaitingForRequestAccess,
            Connected,
            Closed
        }

        private class PendingCommand
        {
            public TaskCompletionSource<IMessageData> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
