namespace DSC.TLink.ITv2.Messages
{
    /// <summary>
    /// Implemented by message types that carry an application sequence number.
    /// The app sequence is the first byte of the message payload and is serialized/deserialized
    /// as a regular property by BinarySerializer. It must be declared first in the implementing class.
    /// 
    /// Upstream middleware uses it to correlate outbound commands with their deferred inbound responses.
    /// </summary>
    internal interface IAppSequenceMessage : IMessageData
    {
        byte AppSequence { get; set; }
    }
}
