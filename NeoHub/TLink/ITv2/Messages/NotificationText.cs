using DSC.TLink.ITv2.Transactions;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    [ITv2Command(Enumerations.ITv2Command.Notification_Text)]
    [SimpleAckTransaction]
    public record NotificationText : IAppSequenceMessage
    {
        public byte AppSequence { get; set; }
        [UnicodeString]
        public string Message { get; init; } = String.Empty;
    }
}
