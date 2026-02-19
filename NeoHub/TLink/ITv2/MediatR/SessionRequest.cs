using DSC.TLink.ITv2.Messages;
using MediatR;

namespace DSC.TLink.ITv2.MediatR
{
    internal record SessionRequest<T> : IRequest<Result<SessionRequestResult>> where T : IMessageData
    {
        public required ITv2Session Session { get; init; }
        public required T MessageData { get; init; }

    }
}
