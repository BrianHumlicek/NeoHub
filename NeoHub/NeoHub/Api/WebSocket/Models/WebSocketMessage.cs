using System.Text.Json.Serialization;
using NeoHub.Services.Models;

namespace NeoHub.Api.WebSocket.Models
{
    // Base message for polymorphic deserialization
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(GetFullStateMessage), "get_full_state")]
    [JsonDerivedType(typeof(ArmAwayMessage), "arm_away")]
    [JsonDerivedType(typeof(ArmHomeMessage), "arm_home")]
    [JsonDerivedType(typeof(ArmNightMessage), "arm_night")]
    [JsonDerivedType(typeof(DisarmMessage), "disarm")]
    [JsonDerivedType(typeof(FullStateMessage), "full_state")]
    [JsonDerivedType(typeof(PartitionUpdateMessage), "partition_update")]
    [JsonDerivedType(typeof(ZoneUpdateMessage), "zone_update")]
    [JsonDerivedType(typeof(ErrorMessage), "error")]
    public abstract record WebSocketMessage;

    #region Client → Server

    public record GetFullStateMessage : WebSocketMessage;

    public abstract record ArmCommandMessage : WebSocketMessage
    {
        public required string SessionId { get; init; }
        public required byte PartitionNumber { get; init; }
        public string? Code { get; init; }
    }

    public record ArmAwayMessage : ArmCommandMessage;
    public record ArmHomeMessage : ArmCommandMessage;
    public record ArmNightMessage : ArmCommandMessage;
    public record DisarmMessage : ArmCommandMessage;

    #endregion

    #region Server → Client Messages

    public record FullStateMessage : WebSocketMessage
    {
        public required List<SessionDto> Sessions { get; init; }
    }

    public record PartitionUpdateMessage : WebSocketMessage
    {
        public required string SessionId { get; init; }
        public required byte PartitionNumber { get; init; }
        public required PartitionStatus Status { get; init; }
    }

    public record ZoneUpdateMessage : WebSocketMessage
    {
        public required string SessionId { get; init; }
        public required byte ZoneNumber { get; init; }
        public required bool Open { get; init; }
    }

    public record ErrorMessage : WebSocketMessage
    {
        public required string Message { get; init; }
    }

    #endregion

    #region DTOs

    public record SessionDto
    {
        public required string SessionId { get; init; }
        public required string Name { get; init; }
        public required List<PartitionDto> Partitions { get; init; }
        public required List<ZoneDto> Zones { get; init; }
    }

    public record PartitionDto
    {
        public required byte PartitionNumber { get; init; }
        public required string Name { get; init; }
        public required PartitionStatus Status { get; init; }
    }

    public record ZoneDto
    {
        public required byte ZoneNumber { get; init; }
        public required string Name { get; init; }
        public required string DeviceClass { get; init; }
        public required bool Open { get; init; }
        public required List<byte> Partitions { get; init; }
    }

    #endregion
}