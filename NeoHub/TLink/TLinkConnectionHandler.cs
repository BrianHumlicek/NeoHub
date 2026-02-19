// DSC TLink - a communications library for DSC Powerseries NEO alarm panels
// Copyright (C) 2024 Brian Humlicek
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using DSC.TLink.ITv2;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DSC.TLink;

internal class ITv2ConnectionHandler : ConnectionHandler
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ITv2ConnectionHandler> _log;

    public ITv2ConnectionHandler(IServiceProvider services, ILogger<ITv2ConnectionHandler> log)
    {
        _services = services;
        _log = log;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        _log.LogInformation("Connection from {RemoteEndPoint}", connection.RemoteEndPoint);

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<IOptions<ITv2Settings>>().Value;
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var sessionManager = scope.ServiceProvider.GetRequiredService<IITv2SessionManager>();

            await using var transport = new TLinkTransport(
                connection.Transport,
                scope.ServiceProvider.GetRequiredService<ILogger<TLinkTransport>>());

            var sessionResult = await ITv2Session.CreateAsync(
                transport, settings,
                scope.ServiceProvider.GetRequiredService<ILogger<ITv2Session>>(),
                mediator,
                connection.ConnectionClosed);

            if (sessionResult.IsFailure)
            {
                _log.LogError("Session creation failed: {Error}", sessionResult.Error);
                return;
            }

            await using var session = sessionResult.Value;
            sessionManager.RegisterSession(session.SessionId, session);

            try
            {
                await foreach (var message in session.GetNotificationsAsync(connection.ConnectionClosed))
                {
                    await PublishNotificationAsync(mediator, session.SessionId, message);
                }
            }
            finally
            {
                sessionManager.UnregisterSession(session.SessionId);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Connection error");
        }
        finally
        {
            _log.LogInformation("Disconnected {RemoteEndPoint}", connection.RemoteEndPoint);
        }
    }

    private static async Task PublishNotificationAsync(
        IMediator mediator, string sessionId, IMessageData message)
    {
        var messageType = message.GetType();
        var notificationType = typeof(SessionNotification<>).MakeGenericType(messageType);
        var notification = Activator.CreateInstance(notificationType, sessionId, message, DateTime.UtcNow);

        if (notification is not null)
            await mediator.Publish(notification);
    }
}
