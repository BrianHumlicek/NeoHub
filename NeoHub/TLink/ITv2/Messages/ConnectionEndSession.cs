using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Connection_End_session)]
    internal record ConnectionEndSession : IMessageData
    {
    }
}
