using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.Transactions;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.ModuleStatus_Command_Request)]
    [CommandRequestTransaction]
    public record CommandRequestMessage : ICommandMessageData
    {
        public byte CorrelationID { get; set; }
        public ITv2Command CommandRequest { get; init; }
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
