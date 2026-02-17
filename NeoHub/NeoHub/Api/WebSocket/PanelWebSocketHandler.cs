using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DSC.TLink.ITv2.MediatR;
using NeoHub.Api.WebSocket.Models;
using NeoHub.Services;

namespace NeoHub.Api.WebSocket
{
    /// <summary>
    /// Manages WebSocket connections for Home Assistant integration.
    /// Pushes partition and zone updates in real-time.
    /// </summary>
    public class PanelWebSocketHandler
    {
        private readonly IPanelStateService _panelState;
        private readonly IPanelCommandService _commandService;
        private readonly IITv2SessionManager _sessionManager;
        private readonly ISessionMonitor _sessionMonitor;
        private readonly ILogger<PanelWebSocketHandler> _logger;
        private readonly ConcurrentBag<System.Net.WebSockets.WebSocket> _connectedClients = new();

        public PanelWebSocketHandler(
            IPanelStateService panelState,
            IPanelCommandService commandService,
            IITv2SessionManager sessionManager,
            ISessionMonitor sessionMonitor,
            ILogger<PanelWebSocketHandler> logger)
        {
            _panelState = panelState;
            _commandService = commandService;
            _sessionManager = sessionManager;
            _sessionMonitor = sessionMonitor;
            _logger = logger;

            _logger.LogInformation("PanelWebSocketHandler initialized. Subscribed to state change events.");

            _sessionMonitor.SessionsChanged += OnSessionsChanged;
            _panelState.PartitionStateChanged += OnPartitionChanged;
            _panelState.ZoneStateChanged += OnZoneChanged;
        }

        public async Task HandleConnectionAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                _logger.LogWarning("Rejected non-WebSocket request to /api/ws from {RemoteIp}", 
                    context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var clientId = Guid.NewGuid().ToString("N")[..8];
            _connectedClients.Add(webSocket);
            
            _logger.LogInformation("WebSocket client {ClientId} connected from {RemoteIp}. Total clients: {Count}",
                clientId, context.Connection.RemoteIpAddress, _connectedClients.Count);

            try
            {
                await ReceiveMessagesAsync(webSocket, clientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket client {ClientId} error", clientId);
            }
            finally
            {
                _connectedClients.TryTake(out _);
                _logger.LogInformation("WebSocket client {ClientId} disconnected. Total clients: {Count}",
                    clientId, _connectedClients.Count);
                
                if (webSocket.State == WebSocketState.Open)
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }

        private async Task ReceiveMessagesAsync(System.Net.WebSockets.WebSocket webSocket, string clientId)
        {
            var buffer = new byte[4096];

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogDebug("Client {ClientId} sent close frame", clientId);
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _logger.LogTrace("Client {ClientId} ← {Json}", clientId, json);
                        await ProcessMessageAsync(webSocket, json, clientId);
                    }
                }
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                // Client disconnected abruptly (network drop, browser close, etc.)
                // This is normal behavior, not an error
                _logger.LogDebug("Client {ClientId} disconnected without close handshake", clientId);
            }
        }

        private async Task ProcessMessageAsync(System.Net.WebSockets.WebSocket webSocket, string json, string clientId)
        {
            try
            {
                var message = JsonSerializer.Deserialize<WebSocketMessage>(json, _jsonOptions);
                
                if (message == null)
                {
                    _logger.LogWarning("Client {ClientId} sent null message", clientId);
                    await SendErrorAsync(webSocket, "Invalid message", clientId);
                    return;
                }

                _logger.LogDebug("Client {ClientId} request: {MessageType}", clientId, message.GetType().Name);

                switch (message)
                {
                    case GetFullStateMessage:
                        await SendFullStateAsync(webSocket, clientId);
                        break;

                    case ArmAwayMessage armAway:
                        await HandleArmCommandAsync(webSocket, armAway, DSC.TLink.ITv2.Enumerations.ArmingMode.AwayArm, clientId);
                        break;

                    case ArmHomeMessage armHome:
                        await HandleArmCommandAsync(webSocket, armHome, DSC.TLink.ITv2.Enumerations.ArmingMode.StayArm, clientId);
                        break;

                    case ArmNightMessage armNight:
                        await HandleArmCommandAsync(webSocket, armNight, DSC.TLink.ITv2.Enumerations.ArmingMode.NightArm, clientId);
                        break;

                    case DisarmMessage disarm:
                        await HandleDisarmCommandAsync(webSocket, disarm, clientId);
                        break;

                    default:
                        _logger.LogWarning("Client {ClientId} sent unknown message type: {MessageType}", clientId, message.GetType().Name);
                        await SendErrorAsync(webSocket, $"Unknown message type: {message.GetType().Name}", clientId);
                        break;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Client {ClientId} sent invalid JSON: {Json}", clientId, json);
                await SendErrorAsync(webSocket, $"Invalid JSON: {ex.Message}", clientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from client {ClientId}: {Json}", clientId, json);
                await SendErrorAsync(webSocket, $"Processing error: {ex.Message}", clientId);
            }
        }

        private async Task SendFullStateAsync(System.Net.WebSockets.WebSocket webSocket, string clientId)
        {
            var sessions = _sessionManager.GetActiveSessions()
                .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
                .Select(sessionId =>
                {
                    var partitions = _panelState.GetPartitions(sessionId);
                    var zones = _panelState.GetZones(sessionId);

                    return new SessionDto
                    {
                        SessionId = sessionId,
                        Name = sessionId,
                        Partitions = partitions
                            .Select(kvp => new PartitionDto
                            {
                                PartitionNumber = kvp.Key,
                                Name = $"Partition {kvp.Key}",
                                Status = kvp.Value.EffectiveStatus
                            })
                            .ToList(),
                        Zones = zones
                            .Select(kvp => new ZoneDto
                            {
                                ZoneNumber = kvp.Key,
                                Name = string.IsNullOrEmpty(kvp.Value.ZoneName) ? $"Zone {kvp.Key}" : kvp.Value.ZoneName,
                                DeviceClass = DetermineDeviceClass(kvp.Value),
                                Open = kvp.Value.IsOpen,
                                Partitions = kvp.Value.Partitions
                            })
                            .ToList()
                    };
                })
                .ToList();

            _logger.LogDebug(
                "Client {ClientId} → full_state: {SessionCount} sessions, {PartitionCount} partitions, {ZoneCount} zones",
                clientId,
                sessions.Count,
                sessions.Sum(s => s.Partitions.Count),
                sessions.Sum(s => s.Zones.Count));

            var message = new FullStateMessage { Sessions = sessions };
            await SendMessageAsync(webSocket, message, clientId);
        }

        private async Task HandleArmCommandAsync(
            System.Net.WebSockets.WebSocket webSocket, 
            ArmCommandMessage command, 
            DSC.TLink.ITv2.Enumerations.ArmingMode mode, 
            string clientId)
        {
            _logger.LogInformation("Client {ClientId} → {Command}: Session={SessionId}, Partition={Partition}",
                clientId, command.GetType().Name, command.SessionId, command.PartitionNumber);

            var result = await _commandService.ArmAsync(command.SessionId, command.PartitionNumber, mode, command.Code);

            if (!result.Success)
            {
                await SendErrorAsync(webSocket, result.ErrorMessage ?? "Command failed", clientId);
            }
        }

        private async Task HandleDisarmCommandAsync(
            System.Net.WebSockets.WebSocket webSocket, 
            DisarmMessage command, 
            string clientId)
        {
            _logger.LogInformation("Client {ClientId} → {Command}: Session={SessionId}, Partition={Partition}",
                clientId, command.GetType().Name, command.SessionId, command.PartitionNumber);

            var result = await _commandService.DisarmAsync(command.SessionId, command.PartitionNumber, command.Code);

            if (!result.Success)
            {
                await SendErrorAsync(webSocket, result.ErrorMessage ?? "Command failed", clientId);
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };

        private async Task SendMessageAsync(System.Net.WebSockets.WebSocket webSocket, WebSocketMessage message, string clientId)
        {
            if (webSocket.State != WebSocketState.Open)
            {
                _logger.LogTrace("Skipping send to client {ClientId} (state: {State})", clientId, webSocket.State);
                return;
            }

            // Serialize using message.GetType() so derived properties are included
            var json = JsonSerializer.Serialize(message, message.GetType(), _jsonOptions);

            _logger.LogTrace("Client {ClientId} → {MessageType}: {Json}", clientId, message.GetType().Name, json);

            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task SendErrorAsync(System.Net.WebSockets.WebSocket webSocket, string errorMessage, string clientId)
        {
            _logger.LogWarning("Client {ClientId} → error: {Message}", clientId, errorMessage);
            await SendMessageAsync(webSocket, new ErrorMessage { Message = errorMessage }, clientId);
        }

        #region Event Handlers (broadcast to all clients)

        private void OnSessionsChanged()
        {
            _logger.LogDebug("Session list changed, broadcasting full_state to {Count} clients", 
                _connectedClients.Count(c => c.State == WebSocketState.Open));
            _ = BroadcastFullStateAsync();
        }

        private void OnPartitionChanged(object? sender, PartitionStateChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.SessionId))
            {
                _logger.LogWarning("Ignoring partition update with empty sessionId");
                return;
            }

            _logger.LogDebug("Broadcasting partition_update: Session={SessionId}, Partition={Partition}, Status={Status}",
                e.SessionId, e.Partition.PartitionNumber, e.Partition.EffectiveStatus);

            var message = new PartitionUpdateMessage
            {
                SessionId = e.SessionId,
                PartitionNumber = e.Partition.PartitionNumber,
                Status = e.Partition.EffectiveStatus
            };

            _ = BroadcastMessageAsync(message);
        }

        private void OnZoneChanged(object? sender, ZoneStateChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.SessionId))
            {
                _logger.LogWarning("Ignoring zone update with empty sessionId");
                return;
            }

            _logger.LogDebug("Broadcasting zone_update: Session={SessionId}, Zone={Zone}, Open={Open}",
                e.SessionId, e.Zone.ZoneNumber, e.Zone.IsOpen);

            var message = new ZoneUpdateMessage
            {
                SessionId = e.SessionId,
                ZoneNumber = e.Zone.ZoneNumber,
                Open = e.Zone.IsOpen
            };

            _ = BroadcastMessageAsync(message);
        }

        private async Task BroadcastFullStateAsync()
        {
            var openClients = _connectedClients.Where(c => c.State == WebSocketState.Open).ToList();
            _logger.LogTrace("Broadcasting full_state to {Count} clients", openClients.Count);

            foreach (var client in openClients)
            {
                try
                {
                    await SendFullStateAsync(client, "broadcast");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting full state to client");
                }
            }
        }

        private async Task BroadcastMessageAsync(WebSocketMessage message)
        {
            var openClients = _connectedClients.Where(c => c.State == WebSocketState.Open).ToList();
            _logger.LogTrace("Broadcasting {MessageType} to {Count} clients", message.GetType().Name, openClients.Count);

            foreach (var client in openClients)
            {
                try
                {
                    await SendMessageAsync(client, message, "broadcast");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error broadcasting {MessageType} to client", message.GetType().Name);
                }
            }
        }

        #endregion

        #region Helpers

        private static string DetermineDeviceClass(Services.Models.ZoneState zone)
        {
            // TODO: Zone type configuration
            return "door";
        }

        #endregion
    }
}