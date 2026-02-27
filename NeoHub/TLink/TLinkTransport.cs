using DSC.TLink.Extensions;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace DSC.TLink;

/// <summary>
/// Concrete TLink framing implementation over an <see cref="IDuplexPipe"/>.
/// Delegates packet extraction and transforms to <see cref="IPacketAdapter"/>,
/// keeping the core byte-stuffing logic universal.
/// </summary>
internal sealed class TLinkTransport : ITLinkTransport
{
    private readonly IDuplexPipe _pipe;
    private readonly IPacketAdapter _adapter;
    private readonly ILogger<TLinkTransport> _logger;
    private ReadOnlyMemory<byte> _defaultHeader;
    private bool _headerCaptured;

    public TLinkTransport(IDuplexPipe pipe, ILogger<TLinkTransport> logger)
        : this(pipe, DefaultPacketAdapter.Instance, logger) { }

    public TLinkTransport(IDuplexPipe pipe, IPacketAdapter adapter, ILogger<TLinkTransport> logger)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ReadOnlyMemory<byte> DefaultHeader => _defaultHeader;

    #region Inbound — IAsyncEnumerable<Result<TLinkMessage>>

    public async IAsyncEnumerable<Result<TLinkMessage>> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await ReadMessageAsync(cancellationToken);
            if (result.IsFailure)
                yield break;

            yield return result;
        }
    }

    public async Task<Result<TLinkMessage>> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        var reader = _pipe.Input;

        while (true)
        {
            ReadResult readResult;
            try
            {
                readResult = await reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Result<TLinkMessage>.Fail(TLinkPacketException.Code.Cancelled, "Read was cancelled");
            }

            var buffer = readResult.Buffer;

            try
            {
                if (_adapter.TryExtractPacket(ref buffer, out var rawPacket))
                {
                    var transformResult = _adapter.TransformInbound(rawPacket);
                    if (transformResult.IsFailure)
                    {
                        _logger.LogWarning("Inbound transform failed: {Error}", transformResult.Error);
                        return Result<TLinkMessage>.Fail(transformResult.Error!.Value);
                    }

                    var parseResult = ParseFrame(transformResult.Value);
                    if (parseResult.IsSuccess && !_headerCaptured)
                    {
                        _defaultHeader = parseResult.Value.Header;
                        _headerCaptured = true;
                    }

                    return parseResult;
                }
            }
            finally
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
            }

            if (readResult.IsCompleted)
            {
                _logger.LogInformation("Transport completed (remote closed)");
                return Result<TLinkMessage>.Fail(TLinkPacketException.Code.Disconnected, "Transport completed");
            }

            // Not enough data yet — loop back to ReadAsync for more bytes
        }
    }
    private static Result<TLinkMessage> ParseFrame(ReadOnlySequence<byte> packetSequence)
    {
        var reader = new SequenceReader<byte>(packetSequence);
        var packetHex = ILoggerExtensions.Enumerable2HexString(packetSequence.ToArray());

        if (!reader.TryReadTo(out ReadOnlySequence<byte> headerSeq, (byte)0x7E, advancePastDelimiter: true))
            return Result<TLinkMessage>.Fail(TLinkPacketException.Code.FramingError, "Missing header delimiter 0x7E", packetHex);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> payloadSeq, (byte)0x7F, advancePastDelimiter: true))
            return Result<TLinkMessage>.Fail(TLinkPacketException.Code.FramingError, "Missing payload delimiter 0x7F", packetHex);

        var headerResult = Unstuff(headerSeq);
        if (headerResult.IsFailure)
            return Result<TLinkMessage>.Fail(headerResult.Error!.Value);

        var payloadResult = Unstuff(payloadSeq);
        if (payloadResult.IsFailure)
            return Result<TLinkMessage>.Fail(payloadResult.Error!.Value);

        return new TLinkMessage(
            Header: headerResult.Value,
            Payload: payloadResult.Value);
    }

    #endregion

    #region Outbound — Send

    public Task<Result> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        => SendAsync(_defaultHeader, payload, cancellationToken);

    public async Task<Result> SendAsync(
        ReadOnlyMemory<byte> header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Build the standard TLink frame (stuffing + delimiters)
        var stuffedHeader = Stuff(header.Span);
        var stuffedPayload = Stuff(payload.Span);

        var framedLength = stuffedHeader.Length + 1 + stuffedPayload.Length + 1;
        var framedPacket = new byte[framedLength];

        stuffedHeader.CopyTo(framedPacket.AsMemory());
        framedPacket[stuffedHeader.Length] = 0x7E;
        stuffedPayload.CopyTo(framedPacket.AsMemory(stuffedHeader.Length + 1));
        framedPacket[^1] = 0x7F;

        // Step 2: Let adapter transform (e.g. DLS encryption + length prefix)
        var transformResult = _adapter.TransformOutbound(framedPacket);
        if (transformResult.IsFailure)
            return Result.Fail(transformResult.Error!.Value);

        // Step 3: Write to pipe
        try
        {
            _logger.LogTrace("Sent {Packet}", transformResult.Value);
            await _pipe.Output.WriteAsync(transformResult.Value, cancellationToken);
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            return Result.Fail(TLinkPacketException.Code.Cancelled, "Send was cancelled");
        }
        catch (Exception ex)
        {
            return Result.Fail(TLinkPacketException.Code.Disconnected, $"Send failed: {ex.Message}");
        }
    }

    #endregion

    #region Byte Stuffing (shared core — same for ITv2 and DLS)

    private static byte[] Stuff(ReadOnlySpan<byte> input)
    {
        var buffer = new ArrayBufferWriter<byte>(input.Length);
        foreach (var b in input)
        {
            switch (b)
            {
                case 0x7D: buffer.Write(new byte[] { 0x7D, 0x00 }); break;
                case 0x7E: buffer.Write(new byte[] { 0x7D, 0x01 }); break;
                case 0x7F: buffer.Write(new byte[] { 0x7D, 0x02 }); break;
                default:   buffer.Write(new byte[] { b });           break;
            }
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static Result<byte[]> Unstuff(ReadOnlySequence<byte> sequence)
    {
        if (sequence.PositionOf((byte)0x7E).HasValue)
            return Result<byte[]>.Fail(TLinkPacketException.Code.EncodingError, "Invalid byte 0x7E in encoded data");
        if (sequence.PositionOf((byte)0x7F).HasValue)
            return Result<byte[]>.Fail(TLinkPacketException.Code.EncodingError, "Invalid byte 0x7F in encoded data");

        var reader = new SequenceReader<byte>(sequence);
        var result = new ArrayBufferWriter<byte>((int)sequence.Length);

        while (!reader.End)
        {
            if (reader.TryReadTo(out ReadOnlySequence<byte> literal, (byte)0x7D, advancePastDelimiter: true))
            {
                foreach (var segment in literal)
                    result.Write(segment.Span);

                if (!reader.TryRead(out byte escaped))
                    return Result<byte[]>.Fail(TLinkPacketException.Code.EncodingError, "Unexpected end after escape byte 0x7D");

                byte decoded = escaped switch
                {
                    0x00 => 0x7D,
                    0x01 => 0x7E,
                    0x02 => 0x7F,
                    _ => 0xFF // sentinel — handled below
                };

                if (escaped > 0x02)
                    return Result<byte[]>.Fail(TLinkPacketException.Code.EncodingError, $"Unknown escape value 0x{escaped:X2}");

                result.Write(new byte[] { decoded });
            }
            else
            {
                foreach (var segment in reader.UnreadSequence)
                    result.Write(segment.Span);
                break;
            }
        }

        return result.WrittenSpan.ToArray();
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        _pipe.Input.Complete();
        _pipe.Output.Complete();
        return ValueTask.CompletedTask;
    }
}