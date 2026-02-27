using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.ModuleStatus_Global_Status)]
    internal record GlobalStatusResponse : IMessageData
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
