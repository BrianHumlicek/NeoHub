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
    
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using DSC.TLink.Extensions;
using DSC.TLink.ITv2.Encryption;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Messages;
using DSC.TLink.ITv2.Transactions;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2;

internal sealed class ITv2Session_old : IITv2Session
{
    private readonly ITLinkTransport _transport;
    private readonly ITv2Settings _settings;
    private readonly ILogger<ITv2Session> _log;

    // Protocol state
    private string _sessionId = null!;
    private byte _localSequence = 1;
    private byte _remoteSequence;
    private byte _appSequence;
    private EncryptionHandler? _encryption;

    // Transaction management — SAME PATTERN as current code
    private readonly List<Transaction> _pendingTransactions = new();
    private readonly SemaphoreSlim _transactionSemaphore = new(1, 1);
    private readonly TaskCompletionSource _flushComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdownCts = new();

    // App-sequence correlation (replaces AppSequenceTransactionRegistry)
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<IMessageData>> _pendingAppSequence = new();

    private readonly ConcurrentQueue<IMessageData> _notifications = new();

    // Background
    private IAsyncEnumerator<Result<TLinkMessage>>? _reader;

    // ─── Construction ──────────────────────────────────────────────────

    private ITv2Session(ITLinkTransport transport, ITv2Settings settings, ILogger<ITv2Session> logger)
    {
        _transport = transport;
        _settings = settings;
        _log = logger;
    }

    public string SessionId => _sessionId;

    /// <summary>
    /// Factory: handshake → start pump → return ready session.
    /// </summary>
    internal static async Task<Result<ITv2Session>> InitializeAsync(
        ITLinkTransport transport, ITv2Settings settings,
        ILogger<ITv2Session> logger, CancellationToken ct)
    {
        var session = new ITv2Session(transport, settings, logger);
        session._reader = transport.ReadAllAsync(ct).GetAsyncEnumerator(ct);

        var handshake = await session.PerformHandshakeAsync(ct);
        if (handshake.IsFailure)
        {
            await session.DisposeAsync();
            return Result<ITv2Session>.Fail(handshake.Error!.Value);
        }

        // No Task.Run — the caller's await foreach drives everything
        logger.LogInformation("Session {SessionId} ready", session._sessionId);
        return session;
    }

    // ─── Handshake (sequential, uses transactions directly) ────────────

    private async Task<Result> PerformHandshakeAsync(CancellationToken ct)
    {
        try
        {
            // First message → OpenSession (unencrypted)
            var tlinkPacket = await GetNextTlinkPacketAsync(ct);
            if (tlinkPacket.IsFailure) return Result.Fail(tlinkPacket.Error!.Value);

            _sessionId = Encoding.UTF8.GetString(tlinkPacket.Value.Header.Span);
            _log.LogInformation("Connection from Integration ID {SessionId}", _sessionId);

            var firstPacketResult = ParsePayload(tlinkPacket.Value.Payload);
            if (firstPacketResult.IsFailure) return Result.Fail(firstPacketResult.Error!.Value);

            var firstPacket = firstPacketResult.Value;
            _remoteSequence = firstPacket.senderSequence;

            var openSession = firstPacket.messageData.As<OpenSession>();
            _log.LogInformation("Open Session: encryption {Type}", openSession.EncryptionType);

            await SendTransactionPacketAsync(SimpleAckPacket.CreateAckFor(firstPacket), ct);

            // OpenSession: inbound then outbound (CommandResponse exchange)
            var openInbound = await ExecuteInboundTransactionAsync(firstPacket, ct);
            if (openInbound.IsFailure) return openInbound;

            var openOutbound = await ExecuteOutboundTransactionAsync(openSession, ct);
            if (openOutbound.IsFailure) return openOutbound;

            // Configure encryption handler
            var encryptionResult = CreateEncryptionHandler(openSession.EncryptionType);
            if (encryptionResult.IsFailure) return Result.Fail(encryptionResult.Error!.Value);
            _encryption = encryptionResult.Value;

            // RequestAccess (still unencrypted inbound)
            var reqResult = await ReadNextPacketAsync(ct);
            if (reqResult.IsFailure) return Result.Fail(reqResult.Error!.Value);

            var reqPacket = reqResult.Value;
            var requestAccess = reqPacket.messageData.As<RequestAccess>();
            _log.LogInformation("Request Access received");

            await SendTransactionPacketAsync(SimpleAckPacket.CreateAckFor(reqPacket), ct);

            // Activate outbound encryption, then ACK
            _encryption.ConfigureOutboundEncryption(requestAccess.Initializer);

            var reqInbound = await ExecuteInboundTransactionAsync(reqPacket, ct);
            if (reqInbound.IsFailure)
            {
                _log.LogError("Problem negotiating encryption. Check Access Code " +
                    "[851][423,450,477,504] (Type1) or [851][700-703] (Type2)");
                return reqInbound;
            }

            // Send our RequestAccess (now encrypted outbound)
            var ourRequestAccess = new RequestAccess
            {
                Initializer = _encryption.ConfigureInboundEncryption()
            };
            var reqOutbound = await ExecuteOutboundTransactionAsync(ourRequestAccess, ct);
            if (reqOutbound.IsFailure)
            {
                _log.LogError("Problem negotiating encryption. Check Access Code " +
                    "[851][423,450,477,504] (Type1) or [851][700-703] (Type2)");
                return reqOutbound;
            }

            return Result.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Handshake failed");
            return Result.Fail(TLinkPacketException.Code.Unknown, $"Handshake failed: {ex.Message}");
        }
    }

    /// <summary>Handshake: run inbound transaction to completion, reading responses sequentially.</summary>
    private async Task<Result> ExecuteInboundTransactionAsync(ITv2MessagePacket inbound, CancellationToken ct)
    {
        EnsureInboundSequence(ref inbound);
        var tx = CreateTransaction(inbound.messageData);
        await tx.BeginInboundAsync(inbound, ct);
        return await DrainTransactionAsync(tx, ct);
    }

    /// <summary>Handshake: run outbound transaction to completion, reading responses sequentially.</summary>
    private async Task<Result> ExecuteOutboundTransactionAsync(IMessageData message, CancellationToken ct)
    {
        var packet = CreateNextOutboundPacket(message);
        var tx = CreateTransaction(message);
        await tx.BeginOutboundAsync(packet, ct);
        return await DrainTransactionAsync(tx, ct);
    }

    /// <summary>Handshake: feed messages to a transaction until it completes.</summary>
    private async Task<Result> DrainTransactionAsync(Transaction tx, CancellationToken ct)
    {
        while (tx.CanContinue)
        {
            var nextResult = await ReadNextPacketAsync(ct);
            if (nextResult.IsFailure)
            {
                tx.Dispose();
                return Result.Fail(nextResult.Error!.Value);
            }
            if (!await tx.TryContinueAsync(nextResult.Value, ct))
            {
                tx.Dispose();
                return Result.Fail(TLinkPacketException.Code.UnexpectedResponse,
                    $"Handshake failed at {nextResult.Value.messageData.GetType().Name}");
            }
        }
        tx.Dispose();
        return Result.Ok();
    }

    // ─── SendAsync (outbound commands) ─────────────────────────────────

    public async Task<Result<IMessageData>> SendAsync(IMessageData command, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var token = linked.Token;

        await _flushComplete.Task.WaitAsync(token);

        var newTransaction = CreateTransaction(command);

        // Register app-sequence tracking BEFORE sending (so pump can match responses)
        TaskCompletionSource<IMessageData>? appSeqTcs = null;

        // Acquire semaphore to access transaction list + send
        if (!await _transactionSemaphore.WaitAsync(TimeSpan.FromSeconds(30), token))
            return Result<IMessageData>.Fail(TLinkPacketException.Code.Timeout, "Transaction lock timeout");

        try
        {
            var packet = CreateNextOutboundPacket(command);

            if (command is IAppSequenceMessage appCmd)
            {
                appSeqTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingAppSequence[appCmd.AppSequence] = appSeqTcs;
            }

            _log.LogMessageDebug("Sending", command);
            await newTransaction.BeginOutboundAsync(packet, token);

            if (newTransaction.CanContinue)
            {
                _pendingTransactions.Add(newTransaction);
                _log.LogDebug("Outbound {TxType} started: {MessageType}",
                    newTransaction.GetType().Name, command.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            // Clean up app-sequence registration on failure
            if (command is IAppSequenceMessage a)
                _pendingAppSequence.TryRemove(a.AppSequence, out _);
            return Result<IMessageData>.Fail(TLinkPacketException.Code.Unknown, ex.Message);
        }
        finally
        {
            _transactionSemaphore.Release();
        }

        // Await result OUTSIDE semaphore — the pump completes this via TryContinueAsync
        var txResult = await newTransaction.Result;

        if (!txResult.Success)
            return Result<IMessageData>.Fail(TLinkPacketException.Code.UnexpectedResponse,
                txResult.ErrorMessage ?? "Transaction failed");

        // For app-sequence commands, await the deferred panel response
        if (appSeqTcs is not null)
        {
            try
            {
                var deferred = await appSeqTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), token);
                _log.LogDebug("AppSequence deferred response: {Type}", deferred.GetType().Name);
                return Result<IMessageData>.Ok(deferred);
            }
            catch (TimeoutException)
            {
                if (command is IAppSequenceMessage a)
                    _pendingAppSequence.TryRemove(a.AppSequence, out _);
                return Result<IMessageData>.Fail(TLinkPacketException.Code.Timeout,
                    "No app-sequence response within 60s");
            }
        }

        return Result<IMessageData>.Ok(txResult.ResponseMessage);
    }

    // ─── Heartbeat ─────────────────────────────────────────────────────

    private void BeginHeartbeat(CancellationToken ct)
    {
        Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(100), ct);
                    var result = await SendAsync(new ConnectionPoll(), ct);
                    if (result.IsFailure)
                        _log.LogWarning("Heartbeat failed: {Error}", result.Error);
                    else
                        _log.LogDebug("Heartbeat sent");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.LogError(ex, "Heartbeat error"); }
        }, ct);
    }

    // ─── Protocol Helpers ──────────────────────────────────────────────

    private Transaction CreateTransaction(IMessageData messageData) =>
        TransactionFactory.CreateTransaction(messageData, _log, SendTransactionPacketAsync);

    /// <summary>
    /// Send delegate given to transactions. They call this with their
    /// own sequence pair — sequences are per-transaction, not per-message.
    /// </summary>
    private async Task<Result> SendTransactionPacketAsync(ITv2MessagePacket packet, CancellationToken ct)
    {
        var bytes = SerializePacket(packet);
        _log.LogTrace("Sending (pre-encrypt) {Bytes}", bytes);
        ITv2Framing.AddFraming(bytes);
        var encrypted = _encryption?.HandleOutboundData(bytes.ToArray()) ?? bytes.ToArray();

        var result = await _transport.SendAsync(encrypted, ct);
        if (result.IsFailure)
            return Result.Fail(TLinkPacketException.Code.Disconnected, result.Error!.Value.Message);
        return Result.Ok();
    }

    private void EnsureInboundSequence(ref ITv2MessagePacket packet)
    {
        if (packet.messageData is IAppSequenceMessage appMsg)
            _appSequence = appMsg.AppSequence;

        if (packet.receiverSequence != _localSequence)
            packet = packet with { receiverSequence = _localSequence };
    }

    /// <summary>
    /// Increments _localSequence ONCE per outbound transaction.
    /// Within the transaction, all messages reuse this sequence pair.
    /// </summary>
    private ITv2MessagePacket CreateNextOutboundPacket(IMessageData messageData)
    {
        if (messageData is IAppSequenceMessage appMsg)
            appMsg.AppSequence = ++_appSequence;

        return new ITv2MessagePacket(
            senderSequence: ++_localSequence,
            receiverSequence: _remoteSequence,
            messageData: messageData);
    }

    private static List<byte> SerializePacket(ITv2MessagePacket packet)
    {
        var bytes = new List<byte>
        {
            packet.senderSequence,
            packet.receiverSequence
        };
        bytes.AddRange(MessageFactory.SerializeMessage(packet.messageData));
        return bytes;
    }

    // ─── Transport I/O ─────────────────────────────────────────────────

    private async Task<Result<TLinkMessage>> GetNextTlinkPacketAsync(CancellationToken ct)
    {
        if (!await _reader!.MoveNextAsync())
            return Result<TLinkMessage>.Fail(TLinkPacketException.Code.Disconnected, "Transport closed");
        return _reader.Current;
    }

    private async Task<Result<ITv2MessagePacket>> ReadNextPacketAsync(CancellationToken ct)
    {
        var tlinkPacket = await GetNextTlinkPacketAsync(ct);
        if (tlinkPacket.IsFailure)
            return Result<ITv2MessagePacket>.Fail(tlinkPacket.Error!.Value);
        var packetResult = ParsePayload(tlinkPacket.Value.Payload);
        if (packetResult.IsFailure)
            return packetResult;
        _remoteSequence = packetResult.Value.senderSequence;
        return packetResult;
    }

    private Result<ITv2MessagePacket> ParsePayload(ReadOnlyMemory<byte> rawPayload)
    {
        var payload = _encryption?.HandleInboundData(rawPayload.ToArray()) ?? rawPayload.ToArray();
        var span = new ReadOnlySpan<byte>(payload);
        var framingResult = ITv2Framing.RemoveFraming(ref span);
        if (framingResult.IsFailure)
            return Result<ITv2MessagePacket>.Fail(framingResult.Error!.Value);

        byte senderSeq = span.PopByte();
        byte receiverSeq = span.PopByte();
        var message = MessageFactory.DeserializeMessage(span);

        return new ITv2MessagePacket(senderSeq, receiverSeq, message);
    }

    private Result<EncryptionHandler> CreateEncryptionHandler(EncryptionType type) => type switch
    {
        EncryptionType.Type1 => new Type1EncryptionHandler(_settings),
        EncryptionType.Type2 => new Type2EncryptionHandler(_settings),
        _ => Result<EncryptionHandler>.Fail(TLinkPacketException.Code.Unknown, $"Unsupported encryption: {type}")
    };

    // ─── Lifecycle ─────────────────────────────────────────────────────

    private void AbortAllPending()
    {
        foreach (var tx in _pendingTransactions)
        {
            try { tx.Abort(); } catch { }
            tx.Dispose();
        }
        _pendingTransactions.Clear();

        foreach (var (_, tcs) in _pendingAppSequence)
            tcs.TrySetCanceled();
        _pendingAppSequence.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        AbortAllPending();

        if (_reader is not null)
            await _reader.DisposeAsync();

        _encryption?.Dispose();
        _transactionSemaphore.Dispose();
        _shutdownCts.Dispose();
    }

    public async IAsyncEnumerable<IMessageData> GetNotificationsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
        var token = linked.Token;

        // Flush detection: 2s silence after reconnect = queue flushed
        using var flushTimer = new Timer(_ =>
        {
            if (_flushComplete.TrySetResult())
            {
                _log.LogInformation("Receive queue flushed — ready to send");
                BeginHeartbeat(token);
            }
        });

        _log.LogInformation("Notification stream started");

        while (await _reader!.MoveNextAsync())
        {
            if (token.IsCancellationRequested) yield break;

            var tlinkResult = _reader.Current;
            if (tlinkResult.IsFailure)
            {
                _log.LogWarning("Bad TLink frame: {Error}", tlinkResult.Error);
                continue;
            }

            if (!_flushComplete.Task.IsCompleted)
                flushTimer.Change(2000, Timeout.Infinite);

            var packetResult = ParsePayload(tlinkResult.Value.Payload);
            if (packetResult.IsFailure)
            {
                _log.LogWarning("Bad ITv2 framing: {Error}", packetResult.Error);
                continue;
            }

            var packet = packetResult.Value;
            _remoteSequence = packet.senderSequence;

            // Dispatch to transactions, collect any notifications
            await DispatchPacketAsync(packet, token);
            _log.LogDebug("Notifications count {count} after dispatchpacket", _notifications.Count);

            // Yield OUTSIDE the semaphore — drain the queue
            while (_notifications.TryDequeue(out var notification))
                yield return notification;
        }

        _log.LogInformation("Transport closed, notification stream ending");
        AbortAllPending();
    }

    /// <summary>
    /// Dispatches one transport packet through the transaction layer.
    /// Returns any inbound messages that should be yielded as notifications.
    /// Outbound transaction results are consumed directly by SendAsync.
    /// </summary>
    private async Task DispatchPacketAsync(ITv2MessagePacket packet, CancellationToken ct)
    {
        if (!await _transactionSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            _log.LogError("Transaction semaphore timeout — possible deadlock");
            return;
        }

        try
        {
            // 1. Try pending transactions — each checks its own sequence correlation
            bool matched = false;
            foreach (var tx in _pendingTransactions)
            {
                if (await tx.TryContinueAsync(packet, ct))
                {
                    matched = true;
                    break;
                }
            }

            // 2. No match → new inbound message
            if (!matched)
            {
                _log.LogMessageDebug("Received", packet.messageData);

                if (packet.messageData is DefaultMessage dm)
                {
                    _log.LogWarning("Unknown command {Command}, data: {Data}",
                        dm.Command, ILoggerExtensions.Enumerable2HexString(dm.Data));
                }

                EnsureInboundSequence(ref packet);
                var newTx = CreateTransaction(packet.messageData);

                _log.LogDebug("New {TxType}: {MessageType}",
                    newTx.GetType().Name, packet.messageData.GetType().Name);

                _ = newTx.Result.ContinueWith(
                    t => CollectInboundResult(t.Result), TaskContinuationOptions.ExecuteSynchronously);
                _log.LogDebug("Begin begininbound");
                await newTx.BeginInboundAsync(packet, ct);
                _log.LogDebug("End begininbound");
                _log.LogDebug("Notifications count {count}", _notifications.Count);

                if (newTx.CanContinue)
                {
                    _pendingTransactions.Add(newTx);
                }
                else
                {
                    newTx.Dispose();
                }
            }

            // 3. Sweep completed transactions
            var completed = _pendingTransactions.Where(tx => !tx.CanContinue).ToList();
            foreach (var tx in completed)
            {
                _pendingTransactions.Remove(tx);
                tx.Dispose();
                _log.LogDebug("Transaction completed: {Type}", tx.GetType().Name);
            }
        }
        finally
        {
            _transactionSemaphore.Release();
        }
    }

    /// <summary>
    /// Unpacks a transaction result, filters app-sequence matches, collects the rest.
    /// </summary>
    private void CollectInboundResult(TransactionResult result)
    {
        if (!result.Success) return;

        var messages = result.InitiatingMessage switch
        {
            MultipleMessagePacket multi => multi.Messages,
            _ => [result.InitiatingMessage]
        };

        foreach (var msg in messages)
        {
            // App-sequence: claimed by a pending SendAsync caller
            if (msg is IAppSequenceMessage appMsg
                && _pendingAppSequence.TryRemove(appMsg.AppSequence, out var tcs))
            {
                _log.LogDebug("AppSequence {Seq} matched by {Type}",
                    appMsg.AppSequence, msg.GetType().Name);
                tcs.TrySetResult(msg);
                continue;
            }
            _notifications.Enqueue(msg);
        }
        _log.LogDebug("Notifications count {count} end of collect", _notifications.Count);
    }
}
