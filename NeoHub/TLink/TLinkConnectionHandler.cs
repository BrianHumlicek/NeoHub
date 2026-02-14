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
                // Create a new scope per connection
                await using var scope = _serviceProvider.CreateAsyncScope();

                // Get scoped instances
                var session = scope.ServiceProvider.GetRequiredService<ITv2Session>();
                
                // Get singleton instances
                var sessionMediator = scope.ServiceProvider.GetRequiredService<SessionMediator>();
                var sessionManager = scope.ServiceProvider.GetRequiredService<IITv2SessionManager>();

                // Initialize the session
                var sessionConnected = await session.InitializeSession(connection.Transport, connection.ConnectionClosed);

                if (!sessionConnected)
                {
                    _log.LogError("Session failed to properly initialize.  Closing connection.");
                    return;
                }

                // Register session for command routing
                sessionManager.RegisterSession(session.SessionID, session);

                try
                {
                    // Create a closure that captures sessionId for publishing
                    var sessionId = session.SessionID;
                    
                    // Listen for messages and publish notifications
                    await session.ListenAsync(
                        transactionResult => sessionMediator.PublishInboundMessage(sessionId, transactionResult),
                        connection.ConnectionClosed);
                }
                finally
                {
                    // Cleanup: unregister session
                    sessionManager.UnregisterSession(session.SessionID);
                }
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
