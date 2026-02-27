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
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace DSC.TLink.DLSProNet;

/// <summary>
/// DLS-specific TLink transport: length-prefixed framing + optional AES-ECB encryption.
/// </summary>
internal sealed class DLSTLinkTransport : TLinkTransport, IDisposable
{
    private readonly Aes _aes = Aes.Create();
    private bool _encryptionActive;

    public DLSTLinkTransport(IDuplexPipe pipe, ILogger<DLSTLinkTransport> logger)
        : base(pipe, logger) { }

    public void ActivateEncryption(byte[] key)
    {
        _aes.Key = key;
        _encryptionActive = true;
    }

    public void DeactivateEncryption() => _encryptionActive = false;

    /// <summary>
    /// DLS packets are wrapped in a 2-byte big-endian length prefix.
    /// Strips the prefix, then scans for the 0x7F TLink delimiter within (or uses
    /// the full length-bounded slice when encrypted, since the delimiter isn't visible).
    /// </summary>
    protected override bool TryExtractPacket(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> packet)
    {
        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadBigEndian(out short encodedLength) || buffer.Length < encodedLength + 2)
        {
            packet = default;
            return false;
        }

        var innerBuffer = buffer.Slice(2, encodedLength);

        if (_encryptionActive)
        {
            packet = innerBuffer;
            buffer = buffer.Slice(buffer.GetPosition(2 + encodedLength));
            return true;
        }

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

    protected override Result<ReadOnlySequence<byte>> TransformInbound(ReadOnlySequence<byte> rawPacket)
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

    protected override Result<byte[]> TransformOutbound(byte[] framedPacket)
    {
        try
        {
            if (_encryptionActive)
                framedPacket = _aes.EncryptEcb(framedPacket, PaddingMode.Zeros);

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

    public override async ValueTask DisposeAsync()
    {
        _aes.Dispose();
        await base.DisposeAsync();
    }
}
