namespace NeoHub.Services.Models
{
    /// <summary>
    /// High-level phases of a panel connection lifecycle,
    /// tracked from session registration through full initialization.
    /// </summary>
    public enum ConnectionPhase
    {
        Disconnected,
        Handshake,
        ReadingConfig,
        ReadingStatus,
        Connected
    }
}
