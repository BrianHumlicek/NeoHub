using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Connection_Encapsulated_Command_for_Multiple_Packets)]
    public record MultipleMessagePacket : IMessageData
    {
        public IMessageData[] Messages { get; init; } = Array.Empty<IMessageData>();
    }
}
