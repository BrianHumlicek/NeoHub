using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Notification_Chime_Broadcast)]
    public record NotificationChimeBroadcast : IMessageData
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
