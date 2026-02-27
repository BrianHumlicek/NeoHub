using System.IO.Pipelines;

namespace DSC.TLink;

/// <summary>
/// Abstraction over the TLink wire protocol (byte-stuffing, 0x7E/0x7F framing).
/// Returns <see cref="Result{T}"/> instead of throwing exceptions for protocol errors.
/// </summary>
public interface ITLinkTransport : IAsyncDisposable
{
    /// <summary>
    /// Reads a single inbound TLink message. Blocks until a complete message arrives.
    /// Returns a failure result on disconnect or cancellation.
    /// </summary>
    Task<Result<TLinkMessage>> ReadMessageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lazily yields inbound TLink messages as they arrive on the wire.
    /// Recoverable errors (framing, encoding) are yielded as failed results.
    /// The stream completes naturally when the remote endpoint disconnects.
    /// </summary>
    IAsyncEnumerable<Result<TLinkMessage>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single framed TLink message using the default header.
    /// </summary>
    Task<Result> SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single framed TLink message with an explicit header.
    /// </summary>
    Task<Result> SendAsync(ReadOnlyMemory<byte> header, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// The header captured from the first inbound message (Integration ID).
    /// </summary>
    ReadOnlyMemory<byte> DefaultHeader { get; }
}

/// <summary>
/// A single TLink message after de-framing and byte-unstuffing.
/// </summary>
public readonly record struct TLinkMessage(
    ReadOnlyMemory<byte> Header,
    ReadOnlyMemory<byte> Payload);