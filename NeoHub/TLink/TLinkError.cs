namespace DSC.TLink;

/// <summary>
/// Represents a TLink operation failure without throwing an exception.
/// Carries the same error codes as <see cref="TLinkPacketException.Code"/>.
/// </summary>
public readonly record struct TLinkError(
    TLinkPacketException.Code Code,
    string Message,
    string? PacketData = null)
{
    public override string ToString() =>
        PacketData is null ? $"{Code}: {Message}" : $"{Code}: {Message} [Packet: {PacketData}]";
}