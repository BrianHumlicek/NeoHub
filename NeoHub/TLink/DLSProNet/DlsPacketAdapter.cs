using System.Buffers;
using System.Security.Cryptography;
using DSC.TLink.Extensions;

namespace DSC.TLink.DLSProNet;

/// <summary>
/// DLS-specific packet handling: length-prefixed framing + optional AES-ECB encryption.
/// Replaces <c>DLSTLinkClient</c> inheritance with composition.
/// </summary>
internal sealed class DlsPacketAdapter : IPacketAdapter, IDisposable
{
    private readonly Aes _aes = Aes.Create();
    private bool _encryptionActive;

    public void ActivateEncryption(byte[] key)
    {
        _aes.Key = key;
        _encryptionActive = true;
    }

    public void DeactivateEncryption() => _encryptionActive = false;

    /// <summary>
    /// DLS packets are wrapped in a 2-byte big-endian length prefix.
    /// Strips the prefix, then scans for the 0x7F TLink delimiter within.
    /// </summary>
    public bool TryExtractPacket(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> packet)
    {
        var reader = new SequenceReader<byte>(buffer);

        // Need at least 2 bytes for the length prefix
        if (!reader.TryReadBigEndian(out short encodedLength) || buffer.Length < encodedLength + 2)
        {
            packet = default;
            return false;
        }

        // Slice past the 2-byte length prefix to get the inner packet
        var innerBuffer = buffer.Slice(2, encodedLength);

        // When encryption is active, the inner bytes are ciphertext — 
        // the 0x7F delimiter won't be visible. Use the length as the boundary.
        if (_encryptionActive)
        {
            packet = innerBuffer;
            buffer = buffer.Slice(buffer.GetPosition(2 + encodedLength));
            return true;
        }

        // When unencrypted, scan for the 0x7F delimiter within the length-bounded slice
        var delimiter = innerBuffer.PositionOf((byte)0x7F);
        if (!delimiter.HasValue)
        {
            packet = default;
            return false;
        }

        var endInclusive = innerBuffer.GetPosition(1, delimiter.Value);
        packet = innerBuffer.Slice(innerBuffer.Start, endInclusive);
        buffer = buffer.Slice(buffer.GetPosition(2 + encodedLength));
        return true;
    }

    /// <summary>
    /// Decrypts inbound packet bytes when encryption is active.
    /// The decrypted bytes contain the standard TLink frame (header|0x7E|payload|0x7F).
    /// </summary>
    public Result<ReadOnlySequence<byte>> TransformInbound(ReadOnlySequence<byte> rawPacket)
    {
        if (!_encryptionActive)
            return rawPacket;

        try
        {
            ReadOnlySpan<byte> cipherText = rawPacket.IsSingleSegment
                ? rawPacket.FirstSpan
                : rawPacket.ToArray();

            byte[] plainText = _aes.DecryptEcb(cipherText, PaddingMode.Zeros);
            return new ReadOnlySequence<byte>(plainText);
        }
        catch (CryptographicException ex)
        {
            return Result<ReadOnlySequence<byte>>.Fail(
                TLinkPacketException.Code.EncodingError,
                $"AES decryption failed: {ex.Message}",
                ILoggerExtensions.Enumerable2HexString(rawPacket.ToArray()));
        }
    }

    /// <summary>
    /// Encrypts outbound packet bytes and prepends the 2-byte length prefix.
    /// </summary>
    public Result<byte[]> TransformOutbound(byte[] framedPacket)
    {
        try
        {
            if (_encryptionActive)
            {
                framedPacket = _aes.EncryptEcb(framedPacket, PaddingMode.Zeros);
            }

            ushort length = (ushort)framedPacket.Length;
            byte[] result = new byte[2 + framedPacket.Length];
            result[0] = length.HighByte();
            result[1] = length.LowByte();
            framedPacket.CopyTo(result, 2);
            return result;
        }
        catch (CryptographicException ex)
        {
            return Result<byte[]>.Fail(
                TLinkPacketException.Code.EncodingError,
                $"AES encryption failed: {ex.Message}");
        }
    }

    public void Dispose() => _aes.Dispose();
}