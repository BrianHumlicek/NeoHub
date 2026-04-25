# Panel-Data Features: Three-Layer Architecture Sketch

**Status**: Notes for future refactor — *not currently implemented*.
**Context**: Captured after the user-list refactor (branch `user-list`) surfaced repeated friction with how panel-data features interact with `SessionState`.

## Problem statement

Features that read/write panel state (users, configuration, partitions, zones) all reach into `SessionState` for some slice of:

- Panel capabilities (`MaxZones`, `MaxPartitions`, `MaxUsers`)
- Domain collections (`Partitions`, `Zones`, `UserList`, `Configuration`)
- Protocol state (`IsInProgrammingMode`, `ConfigLock`)
- Operation progress (`UserList.IsReading`, `UserList.ReadProgress`, etc.)
- Connection metadata (`ConnectedAt`, `ConnectionPhase`)
- Panel telemetry (`PanelDateTime`)

This leaves `SessionState` as a god object and forces every service to take `sessionId` + look up the session + access a specific slice. Pages end up with implicit dependencies on fields they don't own. Operation progress fields persist on shared state that outlives the operation.

`PanelConfigurationState` got closer to a clean shape than `PanelUserListState` did, because Configuration is explicitly scoped (nullable, owned capabilities, self-contained). `UserList` couldn't follow the same pattern cleanly because `MaxUsers` is a panel capability that other pages consume without needing the user list itself.

## Proposed three-layer split

### Layer 1 — Repository (stateless protocol adapter)

```csharp
interface IPanelUsersRepository
{
    Task<PanelUserList> ReadAllAsync(
        string sessionId,
        string masterCode,
        IProgress<ReadProgress>? progress,
        CancellationToken ct);

    Task<PanelUser> WriteAsync(
        string sessionId,
        PanelUser edited,
        PanelUser original,
        string masterCode,
        CancellationToken ct);
}
```

**Principles**:
- No side-channel state writes — results are returned, not deposited on `SessionState`
- Progress flows through `IProgress<T>`, not shared state
- Testable in isolation (mock `IMediator`, assert on return values)
- Access-code verification/scope still goes through `IPanelAccessCodeService`

### Layer 2 — View model (page-owned operation state)

```csharp
class UsersPageViewModel : IDisposable
{
    public PanelUserList? Users { get; private set; }
    public ReadProgress? ActiveRead { get; private set; }  // lives with the page
    public string? CachedMasterCode { get; set; }

    public event Action? StateChanged;

    public async Task ReadAsync() { ... }
    public async Task EditAsync(int userIndex) { ... }
}
```

**Principles**:
- Read/write progress belongs to *this page's current operation*, not to `SessionState`
- Other tabs don't care "am I reading users right now" — move it off session
- Page subscribes to `StateChanged`, not `SessionStateChanged` (fewer spurious renders)
- Survives across edits within the page session, doesn't need to survive nav

**Open question**: If the operator nav-away during a read, should it continue? Current behavior lets it continue via `SessionState.IsReadingUsers`. In the new model, either (a) tie operation lifetime to the page (cancellation on dispose — simpler) or (b) introduce a separate `BackgroundOperationService` if cross-page continuation is actually valued.

### Layer 3 — Data (pure, serializable, no operation state)

```csharp
class PanelUserList
{
    public int MaxUsers { get; }
    public IReadOnlyDictionary<int, PanelUser> Users { get; }
    public DateTime? ReadAt { get; }

    public PanelUser Master => Users[1];
    public IEnumerable<PanelUser> Active => Users.Values.Where(u => u.IsActive);

    public byte[] Serialize();
    public static PanelUserList Deserialize(byte[]);
    public string RenderPrintHtml(bool revealCodes = false, string? sessionId = null);
}
```

**Principles**:
- Serialization lives *on the data*, not in an external file service
- No progress fields, no UI flags, no "reveal codes" toggle state
- Print rendering is a render of the data — the caller decides whether to reveal codes
- Pure value semantics — easy to snapshot, diff (if ever needed), compare

## Migration strategy

**Strangler fig, not big bang.** When the next panel-data feature gets built (bulk ops, cross-panel clone, auto-provisioning, scheduled reads, etc.):

1. Build *that* feature using the three-layer shape
2. Prove the pattern pays off
3. Migrate zones, partitions, config to match when each becomes the path of least resistance

**Don't migrate existing features for their own sake.** `PanelUserService`, `PanelConfigurationService`, `PanelUserListState` are all working. Rewriting them costs days of risk for zero user-visible value.

## What survives from the user-list refactor

The branch that produced these notes is mostly compatible with the future shape:

| Current | Future |
|---|---|
| `IPanelAccessCodeService` | Kept as-is; Layer 1 uses it |
| `AccessCodeDialogExtensions` | Kept as-is; page-level concern |
| `MasterCodeEditDialog` / `UserEditDialog` | Kept as-is |
| `IPanelUserListFileService` methods | Methods move onto `PanelUserList` |
| `PanelUserListState` | Collapses into `PanelUserList` (single type) |
| `IsMaster`, `MasterUserIndex` | Move onto `PanelUser` (same shape) |
| `FindBySessionId` | Stays |
| Audit fixes (mutation-in-delegate, shared lock, etc.) | Stays |

**Rough estimate if we did it**: 3-5 days focused work touching every page, service, and handler. Biggest risk areas: circuit lifecycle (Blazor Server), event firing order, state survival across navigation, and making sure the initial config-pull path (`PanelConfigurationHandler`) still has a sensible home.

## Signals it's time to do this

- Third panel-data feature in a row fights `SessionState`
- Tests for a service become hard to write because of `SessionState` coupling
- Progress tracking for one operation interferes with progress tracking for another
- Cross-page leaks of operation state become real bugs (not just conceptual smells)
- Event-chain re-rendering becomes a measurable performance problem

Until one of those lands, the current shape is good enough.
