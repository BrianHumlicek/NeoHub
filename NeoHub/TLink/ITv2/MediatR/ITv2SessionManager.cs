using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DSC.TLink.ITv2.MediatR;

public interface IITv2SessionManager
{
    IEnumerable<string> GetActiveSessions();
    internal void RegisterSession(string sessionId, IITv2Session session);
    internal void UnregisterSession(string sessionId);
    internal IITv2Session? GetSession(string sessionId);
}

internal class ITv2SessionManager : IITv2SessionManager
{
    private readonly ConcurrentDictionary<string, IITv2Session> _sessions = new();
    private readonly IMediator _mediator;
    private readonly ILogger<ITv2SessionManager> _logger;

    public ITv2SessionManager(IMediator mediator, ILogger<ITv2SessionManager> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public void RegisterSession(string sessionId, IITv2Session session)
    {
        if (_sessions.TryAdd(sessionId, session))
        {
            _logger.LogInformation("Registered session {SessionId}. Active: {Count}", sessionId, _sessions.Count);
            PublishLifecycleAsync(new SessionConnectedNotification(sessionId));
        }
    }

    public void UnregisterSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out _))
        {
            _logger.LogInformation("Unregistered session {SessionId}. Active: {Count}", sessionId, _sessions.Count);
            PublishLifecycleAsync(new SessionDisconnectedNotification(sessionId));
        }
    }

    public IITv2Session? GetSession(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s : null;

    public IEnumerable<string> GetActiveSessions() => _sessions.Keys.ToList();

    private async void PublishLifecycleAsync(INotification notification)
    {
        try { await _mediator.Publish(notification); }
        catch (Exception ex) { _logger.LogError(ex, "Error publishing lifecycle notification"); }
    }
}