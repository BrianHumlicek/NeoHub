using System.Buffers;

namespace DSC.TLink;

/// <summary>
/// Defines how raw bytes are extracted, unwrapped, and wrapped at the packet level.
/// <see cref="TLinkTransport"/> delegates to this for all transport-variant behavior.
/// 
/// Default (ITv2): delimiter-based extraction, no transforms.
/// DLS: length-prefixed extraction, AES encryption/decryption.
/// </summary>
public interface IPacketAdapter
{
    /// <summary>
    /// Tries to extract one complete raw packet from <paramref name="buffer"/>.
    /// On success, advances <paramref name="buffer"/> past the consumed bytes.
    /// </summary>
    bool TryExtractPacket(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> packet);

    /// <summary>
    /// Transforms raw inbound bytes before TLink frame parsing (header/payload split, unstuffing).
    /// Default: pass-through. DLS: AES decryption.
    /// </summary>
    Result<ReadOnlySequence<byte>> TransformInbound(ReadOnlySequence<byte> rawPacket);

    /// <summary>
    /// Transforms a fully-framed outbound packet before writing to the pipe.
    /// Default: pass-through. DLS: AES encryption + length prefix.
    /// </summary>
    Result<byte[]> TransformOutbound(byte[] framedPacket);
}