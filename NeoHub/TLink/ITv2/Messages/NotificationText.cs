using DSC.TLink.ITv2.Transactions;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Notification_Text)]
    [SimpleAckTransaction]
    public record NotificationText : ICommandMessageData
    {
        public byte CorrelationID { get; set; }
        [UnicodeString]
        public string Message { get; init; } = String.Empty;
    }
}
