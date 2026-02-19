using System.Runtime.CompilerServices;
using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.ITv2;

public interface IITv2Session : IAsyncDisposable
{
    string SessionId { get; }
    Task<Result<IMessageData>> SendAsync(IMessageData command, CancellationToken ct = default);
    IAsyncEnumerable<IMessageData> GetNotificationsAsync(CancellationToken ct = default);
}