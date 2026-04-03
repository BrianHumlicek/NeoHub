using DSC.TLink.ITv2.Enumerations;

namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Raw payload wrapper for 0x4736 Access Codes Read Response.
    /// The payload is left untyped on purpose for initial interoperability testing.
    /// </summary>
    [ITv2Command(ITv2Command.Response_Access_Code)]
    public record AccessCodeReadResponse : IMessageData
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }
}
