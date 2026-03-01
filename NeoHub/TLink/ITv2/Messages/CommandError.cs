using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Command_Error)]
    internal record CommandError : IMessageData
    {
        public ITv2Command Command { get; init; }
        public ITv2NackCode NackCode { get; init; }
    }
}
