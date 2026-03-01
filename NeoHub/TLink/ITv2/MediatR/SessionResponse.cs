using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.ITv2.MediatR;

/// <summary>
/// Response from a <see cref="SessionCommand"/>.
/// On success, <see cref="MessageData"/> holds the panel's response (e.g. a <see cref="CommandResponse"/>).
/// On failure, <see cref="ErrorCode"/> identifies the infrastructure error and <see cref="ErrorMessage"/> gives details.
/// </summary>
public record SessionResponse
{
    public bool Success { get; init; }
    public IMessageData? MessageData { get; init; }
    public TLinkErrorCode? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
