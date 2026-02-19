using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.ITv2;

/// <summary>
/// A parsed ITv2 protocol packet (post-decryption, post-framing).
/// </summary>
internal readonly record struct ITv2Packet(
    byte SenderSequence,
    byte ReceiverSequence,
    IMessageData Message);