using DSC.TLink.Extensions;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace DSC.TLink;

/// <summary>
/// Concrete TLink framing implementation over an <see cref="IDuplexPipe"/>.
/// Handles byte-stuffing, 0x7E/0x7F delimiters, and frame parsing.
/// 
/// Subclasses (e.g. DLS) can override packet extraction and transforms
/// for transport-specific framing (length prefixes, encryption, etc.).
/// </summary>
internal class TLinkTransport : ITLinkTransport
{
    private readonly IDuplexPipe _pipe;
    private readonly ILogger _logger;
    private ReadOnlyMemory<byte> _defaultHeader;
    private bool _headerCaptured;

    public TLinkTransport(IDuplexPipe pipe, ILogger<TLinkTransport> logger)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected TLinkTransport(IDuplexPipe pipe, ILogger logger)
    {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ReadOnlyMemory<byte> DefaultHeader => _defaultHeader;

    #region Inbound

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
                return Result<TLinkMessage>.Fail(TLinkErrorCode.Cancelled, "Read was cancelled");
            }

            var buffer = readResult.Buffer;

            try
            {
                if (TryExtractPacket(ref buffer, out var rawPacket))
                {
                    _logger.LogTrace("Recv {Packet}", new HexBytes(rawPacket.ToArray()));

                    var transformResult = TransformInbound(rawPacket);
                    if (transformResult.IsFailure)
                    {
                        _logger.LogWarning("Inbound transform failed: {Error}", transformResult.Error);
                        return Result<TLinkMessage>.Fail(transformResult.Error!.Value);
                    }

                    var parseResult = ParseFrame(transformResult.Value);
                    if (parseResult.IsSuccess)
                    {
                        if (!_headerCaptured)
                        {
                            _defaultHeader = parseResult.Value.Header;
                            _headerCaptured = true;
                        }
                        _logger.LogTrace("Recv payload {Payload}", new HexBytes(parseResult.Value.Payload));
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
                return Result<TLinkMessage>.Fail(TLinkErrorCode.Disconnected, "Transport completed");
            }
        }
    }

    /// <summary>
    /// Attempt to extract a complete packet from the buffer.
    /// Default: scans for the 0x7F TLink delimiter.
    /// </summary>
    protected virtual bool TryExtractPacket(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> packet)
    {
        var position = buffer.PositionOf((byte)0x7F);
        if (!position.HasValue)
        {
            packet = default;
            return false;
        }

        var endInclusive = buffer.GetPosition(1, position.Value);
        packet = buffer.Slice(buffer.Start, endInclusive);
        buffer = buffer.Slice(endInclusive);
        return true;
    }

    /// <summary>
    /// Transform raw inbound packet bytes before frame parsing.
    /// Default: no-op (pass-through).
    /// </summary>
    protected virtual Result<ReadOnlySequence<byte>> TransformInbound(ReadOnlySequence<byte> rawPacket) => rawPacket;

    /// <summary>
    /// Transform framed outbound packet bytes before writing to the pipe.
    /// Default: no-op (pass-through).
    /// </summary>
    protected virtual Result<byte[]> TransformOutbound(byte[] framedPacket) => framedPacket;

    private static Result<TLinkMessage> ParseFrame(ReadOnlySequence<byte> packetSequence)
    {
        var reader = new SequenceReader<byte>(packetSequence);
        var packetHex = new HexBytes(packetSequence.ToArray()).ToString();

        if (!reader.TryReadTo(out ReadOnlySequence<byte> headerSeq, (byte)0x7E, advancePastDelimiter: true))
            return Result<TLinkMessage>.Fail(TLinkErrorCode.FramingError, "Missing header delimiter 0x7E", packetHex);

        if (!reader.TryReadTo(out ReadOnlySequence<byte> payloadSeq, (byte)0x7F, advancePastDelimiter: true))
            return Result<TLinkMessage>.Fail(TLinkErrorCode.FramingError, "Missing payload delimiter 0x7F", packetHex);

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

    #region Outbound

    public Task<Result> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        => SendAsync(_defaultHeader, payload, cancellationToken);

    public async Task<Result> SendAsync(
        ReadOnlyMemory<byte> header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var stuffedHeader = Stuff(header.Span);
        var stuffedPayload = Stuff(payload.Span);

        var framedLength = stuffedHeader.Length + 1 + stuffedPayload.Length + 1;
        var framedPacket = new byte[framedLength];

        stuffedHeader.CopyTo(framedPacket.AsMemory());
        framedPacket[stuffedHeader.Length] = 0x7E;
        stuffedPayload.CopyTo(framedPacket.AsMemory(stuffedHeader.Length + 1));
        framedPacket[^1] = 0x7F;

        var transformResult = TransformOutbound(framedPacket);
        if (transformResult.IsFailure)
            return Result.Fail(transformResult.Error!.Value);

        try
        {
            _logger.LogTrace("Sent {Packet}", new HexBytes(transformResult.Value));
            await _pipe.Output.WriteAsync(transformResult.Value, cancellationToken);
            return Result.Ok();
        }
        catch (OperationCanceledException)
        {
            return Result.Fail(TLinkErrorCode.Cancelled, "Send was cancelled");
        }
        catch (Exception ex)
        {
            return Result.Fail(TLinkErrorCode.Disconnected, $"Send failed: {ex.Message}");
        }
    }

    #endregion

    #region Byte Stuffing

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
            return Result<byte[]>.Fail(TLinkErrorCode.EncodingError, "Invalid byte 0x7E in encoded data");
        if (sequence.PositionOf((byte)0x7F).HasValue)
            return Result<byte[]>.Fail(TLinkErrorCode.EncodingError, "Invalid byte 0x7F in encoded data");

        var reader = new SequenceReader<byte>(sequence);
        var result = new ArrayBufferWriter<byte>((int)sequence.Length);

        while (!reader.End)
        {
            if (reader.TryReadTo(out ReadOnlySequence<byte> literal, (byte)0x7D, advancePastDelimiter: true))
            {
                foreach (var segment in literal)
                    result.Write(segment.Span);

                if (!reader.TryRead(out byte escaped))
                    return Result<byte[]>.Fail(TLinkErrorCode.EncodingError, "Unexpected end after escape byte 0x7D");

                byte decoded = escaped switch
                {
                    0x00 => 0x7D,
                    0x01 => 0x7E,
                    0x02 => 0x7F,
                    _ => 0xFF
                };

                if (escaped > 0x02)
                    return Result<byte[]>.Fail(TLinkErrorCode.EncodingError, $"Unknown escape value 0x{escaped:X2}");

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

    public virtual ValueTask DisposeAsync()
    {
        _pipe.Input.Complete();
        _pipe.Output.Complete();
        return ValueTask.CompletedTask;
    }
}