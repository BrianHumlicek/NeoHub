using System.Buffers;

namespace DSC.TLink;

/// <summary>
/// Standard ITv2 packet handling: scan for 0x7F delimiter, no transforms.
/// </summary>
internal sealed class DefaultPacketAdapter : IPacketAdapter
{
    public static readonly DefaultPacketAdapter Instance = new();

    public bool TryExtractPacket(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> packet)
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

    public Result<ReadOnlySequence<byte>> TransformInbound(ReadOnlySequence<byte> rawPacket) => rawPacket;

    public Result<byte[]> TransformOutbound(byte[] framedPacket) => framedPacket;
}