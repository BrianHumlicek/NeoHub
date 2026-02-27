using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(ITv2Command.Notification_Time_Date_Broadcast)]
    public record NotificationDateTimeBroadcast: IMessageData
    {
        public DateTime DateTime { get; init; }
    }
}
