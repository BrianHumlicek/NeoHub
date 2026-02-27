namespace DSC.TLink;

/// <summary>
/// Represents a TLink operation failure carried in a <see cref="Result"/> or <see cref="Result{T}"/>.
/// </summary>
public readonly record struct TLinkError(
    TLinkErrorCode Code,
    string Message,
    string? PacketData = null)
{
    public override string ToString() =>
        PacketData is null ? $"{Code}: {Message}" : $"{Code}: {Message} [Packet: {PacketData}]";
}