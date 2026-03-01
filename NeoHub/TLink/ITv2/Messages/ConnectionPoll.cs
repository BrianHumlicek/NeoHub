using DSC.TLink.ITv2.Enumerations;
namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Connection_Poll)]
    internal record ConnectionPoll : IMessageData
    {
    }
}
