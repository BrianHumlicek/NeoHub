using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.ModuleStatus_Command_Request)]
    public record CommandRequestMessage : CommandMessageBase
    {
        public ITv2Command CommandRequest { get; init; }
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
