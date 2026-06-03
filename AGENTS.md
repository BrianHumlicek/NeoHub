# NeoHub — Agent Guide

## Project Overview

NeoHub is a real-time web portal for monitoring and controlling **DSC PowerSeries NEO** alarm panels via the **ITv2 (TLink)** protocol. It provides a Blazor Server UI for partition/zone monitoring, arm/disarm control, and a WebSocket API for Home Assistant integration.

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 8 |
| Web framework | ASP.NET Core, Blazor Server (interactive SSR) |
| UI components | MudBlazor 8.x (Material Design) |
| In-process messaging | MediatR 12.x |
| Protocol library | DSC.TLink (in-repo, ITv2 framing + encryption) |
| Containerisation | Docker multi-stage build, GitHub Actions CI |
| Database | None — all state is in-memory |
| Persistence | JSON file (`persist/userSettings.json`) |

## Repository Layout

```
NeoHub.sln                       # Solution root
Dockerfile                       # Multi-stage Docker build
docker-compose.yml               # Local/production compose
docs/                            # Protocol & architecture docs
  ITv2SessionV2-Architecture.md
  TLink-ITv2-Protocol.md
NeoHub/
  TLink/                         # DSC.TLink protocol library (DSC.TLink.csproj)
    ITv2/                        # ITv2 protocol implementation
      Encryption/                # Type1 / Type2 encryption handlers
      Enumerations/              # Protocol enums (ArmingMode, ITv2Command, etc.)
      Messages/                  # Binary message classes (Notification*, Command*, etc.)
      MediatR/                   # SessionNotification<T>, SessionCommand, SessionMediator
    DLSProNet/                   # DLS Pro Net transport
    Serialization/               # Binary serialization helpers
  NeoHub/                        # Blazor web application (NeoHub.csproj)
    Program.cs                   # Application entry point, DI composition root
    Components/
      Layout/                    # MainLayout.razor
      Pages/                     # Home, Connections, Settings, Diagnostics, DevConsole, ZoneDetails
    Api/
      WebSocket/                 # PanelWebSocketHandler — WebSocket API at /api/ws
        Models/                  # DTO types for WebSocket messages
    Services/
      IPanelStateService.cs      # State store interface (sessions, partitions, zones)
      PanelStateService.cs       # In-memory state with event-driven notifications
      IPanelCommandService.cs    # Arm/disarm command interface
      PanelCommandService.cs     # Command execution via ITv2 sessions
      ISessionMonitor.cs         # Active session tracking
      ConnectionSettingsProvider.cs
      Handlers/                  # MediatR notification handlers
        SessionLifecycleHandler.cs
        PartitionStatusNotificationHandler.cs
        ZoneStatusNotificationHandler.cs
        ArmDisarmNotificationHandler.cs
        ExitDelayNotificationHandler.cs
        LabelTextNotificationHandler.cs
        DateTimeBroadcastHandler.cs
        PanelConfigurationHandler.cs
      Settings/                  # Configuration & persistence
      Diagnostics/               # In-app log capture
      Models/                    # PartitionState, ZoneState, SessionState, PartitionStatus
```

## Architecture

The application follows an **event-driven** architecture:

1. **DSC.TLink** listens on a TCP port (default 3072) for panel connections.
2. Panels establish ITv2 sessions with encrypted handshake.
3. Panel messages are deserialized and published as **MediatR notifications** (`SessionNotification<T>`).
4. **Notification handlers** in `Services/Handlers/` transform raw messages into application state via `IPanelStateService`.
5. `PanelStateService` fires C# events (`PartitionStateChanged`, `ZoneStateChanged`, etc.).
6. **Blazor components** subscribe to these events for zero-polling real-time UI updates.
7. **PanelWebSocketHandler** broadcasts state changes to connected WebSocket clients (Home Assistant).

### Key Design Decisions

- All application services are registered as **singletons** (in-memory state store).
- MediatR decouples the protocol layer from business logic — handlers are the bridge.
- The WebSocket API uses `snake_case` JSON serialization for Home Assistant compatibility.
- No database: state lives only in memory, only user settings are persisted to JSON.

## Coding Conventions

### General

- **C# / .NET 8** with `Nullable` enabled and `ImplicitUsings` enabled.
- File-scoped namespaces are NOT used — this project uses block-scoped `namespace X { }`.
- Use `required` and `init` for DTO/event-args properties.
- Constructor injection for all dependencies.
- `ILogger<T>` structured logging with named parameters (e.g. `_logger.LogDebug("Partition {Partition} status: {Status}", ...)`).

### Service Pattern

- Define an interface (`IFooService`) alongside the implementation (`FooService`).
- Register as singleton in `Program.cs`.
- Use `ConcurrentDictionary` for thread-safe state.
- Raise events (`EventHandler<TArgs>`) for state changes instead of polling.

### MediatR Handlers

- One handler per notification type.
- Handler class name = `{NotificationType}Handler`.
- Implement `INotificationHandler<SessionNotification<T>>`.
- Handlers read/update state via `IPanelStateService` and return `Task.CompletedTask`.

### Blazor Components

- Pages live in `Components/Pages/`, layouts in `Components/Layout/`.
- Interactive Server render mode.
- Components subscribe to service events in `OnInitialized`/`OnAfterRender` and call `InvokeAsync(StateHasChanged)`.

### WebSocket API

- Single endpoint: `/api/ws`.
- Messages are polymorphic JSON with `type` discriminator (`snake_case`).
- Client commands: `get_full_state`, `arm_away`, `arm_home`, `arm_night`, `disarm`.
- Server pushes: `full_state`, `partition_update`, `zone_update`, `error`.

## Build & Run

```bash
# Restore and build
dotnet build NeoHub.sln

# Run locally (from NeoHub/NeoHub/)
dotnet run --project NeoHub/NeoHub/NeoHub.csproj

# Docker
docker build -t neohub .
docker run -p 8080:8080 -p 3072:3072 -v ./persist:/app/persist neohub
```

### Ports

| Port | Purpose |
|---|---|
| 8080 | HTTP Web UI |
| 8443 | HTTPS Web UI (optional) |
| 3072 | Panel ITv2 TCP connection (configurable) |

### Configuration

Settings are loaded from `appsettings.json` and overridden by `persist/userSettings.json`. Settings can be edited via the web UI Settings page, which auto-saves to the JSON file.

## Testing

There are currently no test projects in the solution. When adding tests, follow the .NET convention of a separate `NeoHub.Tests` project with xUnit or NUnit.

## Protocol Documentation (`docs/`)

Before working on the DSC.TLink library, ITv2 session logic, encryption, or message handling, **always read the relevant documentation first**:

| Document | Path | Contents |
|---|---|---|
| **TLink & ITv2 Protocol** | `docs/TLink-ITv2-Protocol.md` | Complete protocol reference — TLink framing (byte stuffing, delimiters), ITv2 packet format, sequence numbering, transaction correlation, session handshake (12-step OpenSession/RequestAccess flow), encryption lifecycle (Type1/Type2 ECB), heartbeat/timeouts, MultipleMessagePacket handling, two-level error architecture (CommandError vs CommandResponse), reconnection queue flush behavior |
| **ITv2 Session V2 Architecture** | `docs/ITv2SessionV2-Architecture.md` | Architecture of the ITv2Session implementation — component breakdown (ITv2Session, MessageRouter, MessageReceiver, CommandMessageBase), design decisions (command sequence as property, Router vs inline correlation, IAsyncEnumerable notifications, Result<T> error handling), integration points, testing strategy |

### When to consult these docs

- Modifying anything in `NeoHub/TLink/ITv2/` — read **both** documents
- Adding or changing message types in `ITv2/Messages/` — read the protocol doc (packet format, command vs notification distinction)
- Debugging encryption or handshake issues — read the protocol doc (encryption lifecycle, RequestAccess initializer flow)
- Changing session management or message routing — read the architecture doc (MessageRouter, MessageReceiver, transaction correlation)
- Working with sequence numbers or transaction patterns — read the protocol doc (sequence management, sync vs async command responses)

## Important Notes for AI Agents

- **Do NOT modify `DSC.TLink`** protocol library unless explicitly asked — it handles low-level binary protocol and encryption.
- The `persist/` directory is a Docker volume mount point; never hard-code absolute paths for settings.
- `userSettings.json` structure has sections: `PanelConnections`, `Diagnostics`, `Application`.
- Secrets (integration IDs, access codes) live in `userSettings.json` — never commit real values.
- When adding new notification handlers, register MediatR assemblies in `Program.cs` (already covers both `NeoHub` and `DSC.TLink` assemblies).
- Blazor components must marshal UI updates through `InvokeAsync(StateHasChanged)` since events fire on background threads.
