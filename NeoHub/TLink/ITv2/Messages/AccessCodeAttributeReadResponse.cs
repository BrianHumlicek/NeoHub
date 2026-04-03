using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Raw payload wrapper for 0x4737 Access Code Attributes Read Response.
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code_Attribute)]
    public record AccessCodeAttributeReadResponse : IMessageData
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
