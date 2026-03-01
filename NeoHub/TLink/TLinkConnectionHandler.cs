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
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DSC.TLink
{
    internal class ITv2ConnectionHandler : ConnectionHandler
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ITv2ConnectionHandler> _log;

        public ITv2ConnectionHandler(
            IServiceProvider serviceProvider,
            ILogger<ITv2ConnectionHandler> log)
        {
            _serviceProvider = serviceProvider;
            _log = log;
        }

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            _log.LogInformation("Connection request from {RemoteEndPoint}", connection.RemoteEndPoint);

            try
            {
                var settings = _serviceProvider.GetRequiredService<ITv2Settings>();
                var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
                var sessionMediator = _serviceProvider.GetRequiredService<SessionMediator>();
                var sessionManager = _serviceProvider.GetRequiredService<IITv2SessionManager>();

                var result = await ITv2Session.CreateAsync(
                    connection.Transport, settings, loggerFactory, connection.ConnectionClosed);

                if (result.IsFailure)
                {
                    _log.LogError("Session failed to initialize: {Error}", result.Error);
                    return;
                }

                await using var session = result.Value;

                sessionManager.RegisterSession(session.SessionId, session);
                try
                {
                    await foreach (var message in session.GetNotificationsAsync(connection.ConnectionClosed))
                    {
                        await sessionMediator.PublishNotificationAsync(session.SessionId, message);
                    }
                }
                finally
                {
                    sessionManager.UnregisterSession(session.SessionId);
                }
            }
            catch (OperationCanceledException) when (connection.ConnectionClosed.IsCancellationRequested)
            {
                _log.LogInformation("Connection cancelled");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "ITv2 connection error");
            }
            finally
            {
                _log.LogInformation("TLink disconnected from {RemoteEndPoint}", connection.RemoteEndPoint);
            }
        }
    }
}
