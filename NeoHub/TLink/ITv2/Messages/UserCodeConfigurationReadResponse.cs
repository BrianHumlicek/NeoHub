using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Raw payload wrapper for 0x473C User Code Configuration Read Response.
    /// </summary>
    [ITv2Command(ITv2Command.Response_User_Code_Configuration)]
    public record UserCodeConfigurationReadResponse : IMessageData
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
