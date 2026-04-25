using System.Collections.Concurrent;

namespace NeoHub.Services.Models;

/// <summary>
/// Wraps everything that makes up "the panel's user list as NeoHub knows it" — the slots
/// themselves plus read-operation metadata. One instance per session, stored on
/// <see cref="SessionState.UserList"/>. Parallels <see cref="PanelConfiguration.PanelConfigurationState"/>.
/// </summary>
/// <remarks>
/// The panel-level capability <see cref="SessionState.MaxUsers"/> stays on the session because it
/// is a capability fact reported at handshake time, not scoped to the user-read operation.
/// </remarks>
public class PanelUserListState
{
    /// <summary>
    /// All user slots keyed by 1-based <see cref="PanelUserState.UserIndex"/>. Backed by a
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> so the UI can enumerate it while the
    /// service is populating it during a read.
    /// </summary>
    public ConcurrentDictionary<int, PanelUserState> Users { get; } = new();

    /// <summary>Wall-clock time of the last successful full read.</summary>
    public DateTime? LastReadAt { get; set; }

    // ── Read progress (cleared by PanelUserService at end of ReadAllAsync) ──

    public bool IsReading { get; set; }
    public int ReadCurrent { get; set; }
    public int ReadTotal { get; set; }
    public string? ReadProgress { get; set; }
}
