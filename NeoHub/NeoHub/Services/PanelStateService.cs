using System.Collections.Concurrent;
using NeoHub.Services.Models;

namespace NeoHub.Services
{
    public class PanelStateService : IPanelStateService
    {
        private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
        private readonly ILogger<PanelStateService> _logger;

        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
        public event EventHandler<PartitionStateChangedEventArgs>? PartitionStateChanged;
        public event EventHandler<ZoneStateChangedEventArgs>? ZoneStateChanged;
        public event EventHandler<ConfigurationCompleteEventArgs>? ConfigurationComplete;

        public PanelStateService(ILogger<PanelStateService> logger)
        {
            _logger = logger;
        }

        #region Session Operations

        public SessionState? GetSession(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }

        public IReadOnlyDictionary<string, SessionState> GetAllSessions()
        {
            return _sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private SessionState GetOrCreateSession(string sessionId)
        {
            return _sessions.GetOrAdd(sessionId, id => new SessionState { SessionId = id });
        }

        public void UpdateSession(string sessionId, Action<SessionState> update)
        {
            var session = GetOrCreateSession(sessionId);
            update(session);
            session.LastUpdated = DateTime.UtcNow;
            session.LastMessageAt = DateTime.UtcNow;

            _logger.LogDebug("Updated session {Session}", sessionId);

            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs
            {
                SessionId = sessionId,
                Session = session
            });
        }

        public void RemoveSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out _))
                _logger.LogInformation("Removed session state for {Session}", sessionId);
        }

        #endregion

        #region Partition Operations

        public PartitionState? GetPartition(string sessionId, byte partitionNumber)
        {
            var session = GetSession(sessionId);
            return session?.Partitions.TryGetValue(partitionNumber, out var partition) == true ? partition : null;
        }

        public IReadOnlyDictionary<byte, PartitionState> GetPartitions(string sessionId)
        {
            var session = GetSession(sessionId);
            return session?.Partitions ?? new Dictionary<byte, PartitionState>();
        }

        public void UpdatePartition(string sessionId, PartitionState partition)
        {
            var session = GetOrCreateSession(sessionId);
            session.Partitions[partition.PartitionNumber] = partition;
            session.LastUpdated = DateTime.UtcNow;
            session.LastMessageAt = DateTime.UtcNow;

            _logger.LogDebug("Updated partition {Partition} for session {Session}",
                partition.PartitionNumber, sessionId);

            PartitionStateChanged?.Invoke(this, new PartitionStateChangedEventArgs
            {
                SessionId = sessionId,
                Partition = partition
            });
        }

        #endregion

        #region Zone Operations

        public ZoneState? GetZone(string sessionId, byte zoneNumber)
        {
            var session = GetSession(sessionId);
            return session?.Zones.TryGetValue(zoneNumber, out var zone) == true ? zone : null;
        }

        public IReadOnlyDictionary<byte, ZoneState> GetZones(string sessionId)
        {
            var session = GetSession(sessionId);
            return session?.Zones ?? new Dictionary<byte, ZoneState>();
        }

        public void UpdateZone(string sessionId, ZoneState zone)
        {
            var session = GetOrCreateSession(sessionId);
            session.Zones[zone.ZoneNumber] = zone;
            session.LastUpdated = DateTime.UtcNow;
            session.LastMessageAt = DateTime.UtcNow;

            _logger.LogDebug("Updated zone {Zone} for session {Session}",
                zone.ZoneNumber, sessionId);

            ZoneStateChanged?.Invoke(this, new ZoneStateChangedEventArgs
            {
                SessionId = sessionId,
                Zone = zone
            });
        }

        #endregion

        #region Configuration Complete

        public void OnConfigurationComplete(string sessionId)
        {
            _logger.LogInformation("Configuration complete");
            ConfigurationComplete?.Invoke(this, new ConfigurationCompleteEventArgs { SessionId = sessionId });
        }

        #endregion
    }
}