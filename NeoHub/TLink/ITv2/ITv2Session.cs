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
using Microsoft.Extensions.Logging;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DSC.TLink.ITv2;

/// <summary>
/// An ITv2 protocol session over TLink transport.
/// Manages session state, encryption, sequence numbers, and message routing.
/// 
/// Lifecycle:
///   var result = await ITv2Session.CreateAsync(pipe, settings, logger, ct);
///   if (result.IsSuccess)
///   {
///       var session = result.Value;
///       await foreach (var notification in session.GetNotificationsAsync(ct))
///           // handle notification
///   }
/// </summary>
internal sealed class ITv2Session : IITv2Session
{
    private readonly ITLinkTransport _transport;
    private readonly ITv2Settings _settings;
    private readonly ILogger<ITv2Session> _logger;
    private readonly Channel<IMessageData> _notificationChannel;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    // Pending receivers for outbound messages awaiting responses
    private readonly List<MessageReceiver> _pendingReceivers = new();

    // Sequence state
    private byte _localSequence = 1;
    private byte _remoteSequence = 0;
    private byte _commandSequence = 0;

    // Encryption state
    private EncryptionHandler? _encryption;

    // Reconnection queue flush
    private readonly TaskCompletionSource _queueFlushed = new();
    private Timer? _flushTimer;

    private ITv2Session(
        ITLinkTransport transport,
        ITv2Settings settings,
        ILogger<ITv2Session> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _notificationChannel = Channel.CreateUnbounded<IMessageData>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _flushTimer = new Timer(_ =>
        {
            _logger.LogInformation("Receive queue flushed, ready to send");
            _queueFlushed.TrySetResult();
            _flushTimer?.Dispose();
            _flushTimer = null;
        });
    }

    public string SessionId { get; private set; } = string.Empty;

    #region Factory + Handshake

    /// <summary>
    /// Establish a new ITv2 session by performing the handshake sequence.
    /// Returns a connected session or a failure result.
    /// </summary>
    public static async Task<Result<ITv2Session>> CreateAsync(
        IDuplexPipe pipe,
        ITv2Settings settings,
        ILoggerFactory loggerFactory,
        CancellationToken ct = default)
    {
        var transport = new TLinkTransport(pipe, loggerFactory.CreateLogger<TLinkTransport>());
        var session = new ITv2Session(transport, settings, loggerFactory.CreateLogger<ITv2Session>());

        var handshakeResult = await session.PerformHandshakeAsync(ct);
        if (handshakeResult.IsFailure)
            return Result<ITv2Session>.Fail(handshakeResult.Error!.Value);

        session._logger.LogInformation("Session {SessionId} connected successfully", session.SessionId);

        _ = session.ReceivePumpAsync();
        _ = session.HeartbeatLoopAsync();

        return session;
    }

    private async Task<Result> PerformHandshakeAsync(CancellationToken ct)
    {
        try
        {
            return await DoHandshakeAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return Result.Fail(TLinkErrorCode.Cancelled, "Handshake was cancelled");
        }
        catch (Exception ex)
        {
            return Result.Fail(TLinkErrorCode.PacketParseError, $"Handshake failed: {ex.Message}");
        }
    }

    private async Task<Result> DoHandshakeAsync(CancellationToken ct)
    {
        async Task AckInboundCommandAsync(byte commandSequence, byte remoteSenderSeq)
        {
            var response = new CommandResponse { CommandSequence = commandSequence };
            await SendPacketAsync(new ITv2Packet(_localSequence, remoteSenderSeq, response), ct);
        }

        // Send an outbound command and complete the transaction (receive CommandResponse + send SimpleAck).
        async Task SendCommandAsync(ICommandMessage message)
        {
            message.CommandSequence = GetNextCommandSequence();
            var senderSeq = GetNextLocalSequence();
            await SendPacketAsync(new ITv2Packet(senderSeq, _remoteSequence, (IMessageData)message), ct);

            // Receive CommandResponse
            var responsePacket = await ReceivePacketAsync(ct);
            // Send SimpleAck to close the transaction
            await SendSimpleAckAsync(responsePacket.SenderSequence, ct);
        }

        // Steps 1-3: Panel → Us: OpenSession command transaction
        // 1. Receive OpenSession
        var initialPacket = await ReceivePacketAsync(ct);

        SessionId = System.Text.Encoding.UTF8.GetString(_transport.DefaultHeader.Span);
        _logger.LogInformation("Connection from Integration Identification Number [851][422]: {SessionId}", SessionId);
        OpenSession openSession = initialPacket.Message.As<OpenSession>();
        _commandSequence = openSession.CommandSequence;

        _logger.LogInformation("Handshake: OpenSession with encryption type {Type}", openSession.EncryptionType);
        // 2. Send CommandResponse
        await AckInboundCommandAsync(openSession.CommandSequence, initialPacket.SenderSequence);
        // 3. Receive SimpleAck
        await ReceivePacketAsync(ct);

        // Steps 4-6: Us → Panel: Echo OpenSession command transaction
        await SendCommandAsync(openSession);

        // Initialize encryption handler (not yet active on the wire)
        SetEncryptionHandler(openSession.EncryptionType);

        // Steps 7-9: Panel → Us: RequestAccess command transaction
        // 7. Receive RequestAccess
        var requestAccessPacket = await ReceivePacketAsync(ct);
        RequestAccess requestAccess = requestAccessPacket.Message.As<RequestAccess>();

        _logger.LogInformation("Handshake: RequestAccess received");

        // Configure outbound encryption IMMEDIATELY — step 8 is encrypted
        _encryption!.ConfigureOutboundEncryption(requestAccess.Initializer);

        // 8. Send CommandResponse (encrypted — first encrypted message)
        await AckInboundCommandAsync(requestAccess.CommandSequence, requestAccessPacket.SenderSequence);
        // 9. Receive SimpleAck
        await ReceivePacketAsync(ct);

        // Steps 10-12: Us → Panel: RequestAccess command transaction
        var ourRequestAccess = new RequestAccess { Initializer = _encryption.ConfigureInboundEncryption() };
        await SendCommandAsync(ourRequestAccess);

        _logger.LogInformation("Handshake complete, encryption active");
        return Result.Ok();
    }

    #endregion

    #region Public API

    public async Task<Result<IMessageData>> SendAsync(IMessageData message, CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        await _queueFlushed.Task.WaitAsync(linkedCts.Token);
        return await SendMessageAsync(message, linkedCts.Token);
    }

    public async IAsyncEnumerable<IMessageData> GetNotificationsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        await foreach (var notification in _notificationChannel.Reader.ReadAllAsync(linkedCts.Token))
        {
            yield return notification;
        }
    }

    #endregion

    #region Receive Pump

    private async Task ReceivePumpAsync()
    {
        var ct = _shutdownCts.Token;
        try
        {
            _flushTimer!.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

            await foreach (var tlinkResult in _transport.ReadAllAsync(ct))
            {
                _flushTimer?.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

                if (tlinkResult.IsFailure)
                {
                    _logger.LogWarning("TLink error: {Error}", tlinkResult.Error);
                    continue;
                }

                var packetResult = ParseITv2Packet(tlinkResult.Value.Payload);
                if (packetResult.IsFailure)
                {
                    _logger.LogWarning("ITv2 parse error: {Error}", packetResult.Error);
                    continue;
                }

                var packet = packetResult.Value;
                _remoteSequence = packet.SenderSequence;

                if (packet.Message is not SimpleAck)
                {
                    // Every non-ack inbound message gets an ack
                    // (We don't support inbound command transactions that would need a CommandResponse)
                    await SendSimpleAckAsync(packet.SenderSequence, ct);
                }

                if (_pendingReceivers.Any(receiver => receiver.TryReceive(packet)))
                {
                    CleanupCompletedReceivers();
                    continue;
                }

                // Not matched — publish as notification(s)
                await PublishNotificationsAsync(packet, ct);
            }

            _logger.LogInformation("Transport completed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Receive pump cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in receive pump");
        }
        finally
        {
            _notificationChannel.Writer.Complete();
        }
    }

    /// <summary>
    /// Publish an inbound message (or expanded multi-message sub-messages) as notifications.
    /// Embedded command responses are routed to pending receivers instead of published.
    /// </summary>
    private async Task PublishNotificationsAsync(ITv2Packet packet, CancellationToken ct)
    {
        if (packet.Message is MultipleMessagePacket multiMsg)
        {
            _logger.LogDebug("Expanding MultipleMessagePacket: {Count} sub-messages", multiMsg.Messages.Length);

            foreach (var subMessage in multiMsg.Messages)
            {
                if (subMessage is ICommandMessage commandMessage && _pendingReceivers.Any(receiver => receiver.TryReceive(commandMessage)))
                {
                    _logger.LogDebug("Routed embedded command response: {MessageType}", subMessage.GetType().Name);
                    CleanupCompletedReceivers();
                    continue;
                }

                _logger.LogDebug("Publishing notification: {MessageType}", subMessage.GetType().Name);
                await _notificationChannel.Writer.WriteAsync(subMessage, ct);
            }
        }
        else
        {
            _logger.LogDebug("Publishing notification: {MessageType}", packet.Message.GetType().Name);
            await _notificationChannel.Writer.WriteAsync(packet.Message, ct);
        }
    }

    private void CleanupCompletedReceivers()
    {
        _pendingReceivers.RemoveAll(r => r.IsCompleted);
    }

    #endregion

    #region Send/Receive Primitives

    /// <summary>
    /// Send a message and wait for the transaction to complete.
    /// For notifications: waits for SimpleAck, returns null.
    /// For commands: waits for command response (sync or async), returns the response.
    /// 
    /// The send lock protects only the send operation, not the response await.
    /// This allows the receive pump to process other messages while we wait.
    /// </summary>
    private async Task<Result<IMessageData>> SendMessageAsync(IMessageData message, CancellationToken ct)
    {
        MessageReceiver receiver;

        // Lock only protects building/sending the packet and registering the receiver.
        // The response await happens outside the lock.
        await _sendLock.WaitAsync(ct);
        try
        {
            var senderSeq = GetNextLocalSequence();

            if (message is ICommandMessage cmd)
            {
                var cmdSeq = GetNextCommandSequence();
                cmd.CommandSequence = cmdSeq;
                receiver = MessageReceiver.CreateCommandReceiver(senderSeq, cmdSeq);
            }
            else
            {
                receiver = MessageReceiver.CreateNotificationReceiver(senderSeq);
            }

            _pendingReceivers.Add(receiver);

            var packet = new ITv2Packet(senderSeq, _remoteSequence, message);
            var sendResult = await SendPacketAsync(packet, ct);

            if (sendResult.IsFailure)
            {
                receiver.Dispose();
                _pendingReceivers.Remove(receiver);
                return Result<IMessageData>.Fail(sendResult.Error!.Value);
            }
        }
        finally
        {
            _sendLock.Release();
        }

        // Await response outside the lock so heartbeats and other sends aren't blocked
        var response = await receiver.Result(ct);
        return Result<IMessageData>.Ok(response ?? message);
    }

    private async Task<Result> SendPacketAsync(ITv2Packet packet, CancellationToken ct)
    {
        var plaintext = SerializePacket(packet);

        _logger.LogDebug("TX [{SenderSeq}→{ReceiverSeq}] {MessageType}",
            packet.SenderSequence, packet.ReceiverSequence, packet.Message.GetType().Name);
        _logger.LogTrace("TX bytes: {Bytes}", new HexBytes(plaintext));

        var encoded = _encryption?.HandleOutboundData(plaintext) ?? plaintext;
        return await _transport.SendAsync(encoded, ct);
    }

    private async Task SendSimpleAckAsync(byte remoteSenderSeq, CancellationToken ct)
    {
        var ack = new ITv2Packet(_localSequence, remoteSenderSeq, new SimpleAck());
        await SendPacketAsync(ack, ct);
    }

    private async Task<ITv2Packet> ReceivePacketAsync(CancellationToken ct)
    {
        while (true)
        {
            var tlinkResult = await _transport.ReadMessageAsync(ct);
            if (tlinkResult.IsFailure)
                throw new InvalidOperationException($"Transport error during handshake: {tlinkResult.Error}");

            var packetResult = ParseITv2Packet(tlinkResult.Value.Payload);
            if (packetResult.IsSuccess)
            {
                _remoteSequence = packetResult.Value.SenderSequence;
                return packetResult.Value;
            }

            _logger.LogWarning("ITv2 parse error: {Error}", packetResult.Error);
        }
    }

    #endregion

    #region Serialization

    private byte[] SerializePacket(ITv2Packet packet)
    {
        var bytes = new List<byte>
        ([
            packet.SenderSequence,
            packet.ReceiverSequence,
            ..packet.Message.Serialize()
        ]);

        ITv2Framing.AddFraming(bytes);
        return bytes.ToArray();
    }

    private Result<ITv2Packet> ParseITv2Packet(ReadOnlyMemory<byte> payload)
    {
        try
        {
            var decrypted = _encryption?.HandleInboundData(payload.ToArray()) ?? payload.ToArray();
            var span = new ReadOnlySpan<byte>(decrypted);

            ITv2Framing.RemoveFraming(ref span);

            byte senderSeq = span.PopByte();
            byte receiverSeq = span.PopByte();
            var message = MessageFactory.DeserializeMessage(span);

            _logger.LogDebug("RX [{SenderSeq}→{ReceiverSeq}] {MessageType}",
                senderSeq, receiverSeq, message.GetType().Name);
            _logger.LogTrace("RX bytes: {Bytes}", new HexBytes(decrypted));

            if (_logger.IsEnabled(LogLevel.Debug) && message is not SimpleAck)
                _logger.LogDebug("RX detail: {Message}", new MessageLog(message));

            return new ITv2Packet(senderSeq, receiverSeq, message);
        }
        catch (Exception ex)
        {
            return Result<ITv2Packet>.Fail(
                TLinkErrorCode.PacketParseError,
                $"Failed to parse ITv2 packet: {ex.Message}");
        }
    }

    #endregion

    #region State Management

    private byte GetNextLocalSequence() => ++_localSequence;
    private byte GetNextCommandSequence() => ++_commandSequence;

    private void SetEncryptionHandler(EncryptionType type)
    {
        if (_encryption is not null)
            throw new InvalidOperationException("Encryption already initialized");

        _encryption = type switch
        {
            EncryptionType.Type1 => new Type1EncryptionHandler(_settings),
            EncryptionType.Type2 => new Type2EncryptionHandler(_settings),
            _ => throw new NotSupportedException($"Unsupported encryption type: {type}")
        };

        _logger.LogInformation("Encryption handler initialized: {Type}", type);
    }

    #endregion

    #region Heartbeat

    private async Task HeartbeatLoopAsync()
    {
        var ct = _shutdownCts.Token;
        try
        {
            await _queueFlushed.Task.WaitAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(100), ct);
                await SendAsync(new ConnectionPoll(), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Heartbeat loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in heartbeat loop");
        }
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();

        _flushTimer?.Dispose();
        _notificationChannel.Writer.Complete();

        foreach (var receiver in _pendingReceivers)
            receiver.Dispose();
        _pendingReceivers.Clear();

        _sendLock.Dispose();
        _shutdownCts.Dispose();
        _encryption?.Dispose();

        await _transport.DisposeAsync();
    }

    #endregion
}
