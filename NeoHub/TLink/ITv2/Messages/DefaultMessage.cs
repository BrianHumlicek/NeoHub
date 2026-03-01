using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.Serialization;

namespace DSC.TLink.ITv2.Messages
{
    internal record DefaultMessage : IMessageData
    {
        [IgnoreProperty]
        public ITv2Command Command { get; set; } = ITv2Command.Unknown;
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
