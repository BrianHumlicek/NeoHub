using DSC.TLink.ITv2.Messages;
using MediatR;

namespace DSC.TLink.ITv2.MediatR;

/// <summary>
/// Command to send a message to a specific panel session.
/// Response is <see cref="Result{T}"/> of <see cref="IMessageData"/> —
/// protocol transactions and app-sequence correlation are handled by the session.
/// </summary>
public record SessionCommand : IRequest<Result<IMessageData>>
{
    public required string SessionID { get; init; }
    public required IMessageData MessageData { get; init; }
}
