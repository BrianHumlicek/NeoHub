using System.Collections.Concurrent;
using DSC.TLink.ITv2;
using DSC.TLink.ITv2.Enumerations;
using DSC.TLink.ITv2.MediatR;
using DSC.TLink.ITv2.Messages;
using MediatR;
using Microsoft.Extensions.Options;
using NeoHub.Services.Settings;

namespace NeoHub.Services;

/// <summary>
/// Identifies which panel access code we're dealing with. Drives:
/// - which field on <see cref="ConnectionSettings"/> is read/persisted
/// - which <see cref="ProgrammingMode"/> is used when entering the panel
/// </summary>
public enum PanelAccessCodeKind
{
    Installer,
    Master,
}

/// <summary>Result of an authenticated panel operation. On failure, <see cref="Result"/> is default.</summary>
public record PanelAccessResult<T>(bool Success, T? Result = default, string? ErrorMessage = null);

/// <summary>
/// Single entry point for everything related to a panel's access codes:
/// <list type="bullet">
/// <item>Format validation (4/6/8 numeric digits)</item>
/// <item>Session-scoped caching (prevents re-prompting for the same code across page visits)</item>
/// <item>Connection settings read/update/persist</item>
/// <item>Authenticated panel operations via <see cref="ExecuteAsync"/> — owns the shared
///       panel-mode lock, enters programming mode with the correct privilege, runs the caller's
///       delegate, and always exits + releases.</item>
/// </list>
/// Pages typically interact via the <c>IDialogService.ResolveAccessCodeAsync</c> extension, which
/// walks the cache → settings → prompt ladder and returns a verified code string.
/// </summary>
public interface IPanelAccessCodeService
{
    // ── Format ───────────────────────────────────────────────────────────────
    bool IsValidFormat(string? code);

    /// <summary>Returns null for valid input, or a human-readable error for invalid.</summary>
    string? ValidateFormat(string? code);

    // ── Connection settings ──────────────────────────────────────────────────

    /// <summary>Returns the configured code for the session (or null if no matching connection / empty).</summary>
    string? GetConnectionCode(string sessionId, PanelAccessCodeKind kind);

    /// <summary>
    /// Updates and persists the matching access code on the connection settings entry whose
    /// <c>SessionId</c> matches (case-insensitive). Also updates the in-memory cache. No-ops if
    /// no matching connection exists or the value is already current.
    /// </summary>
    Task<bool> UpdateConnectionCodeAsync(string sessionId, PanelAccessCodeKind kind, string? newCode);

    // ── Session cache ────────────────────────────────────────────────────────

    bool TryGetCached(string sessionId, PanelAccessCodeKind kind, out string? code);
    void SetCached(string sessionId, PanelAccessCodeKind kind, string? code);
    void InvalidateCache(string sessionId, PanelAccessCodeKind kind);

    // ── Authenticated operations ─────────────────────────────────────────────

    /// <summary>
    /// Acquires <see cref="SessionState.ConfigLock"/>, enters the programming mode corresponding
    /// to <paramref name="kind"/> authenticated with <paramref name="code"/>, runs
    /// <paramref name="operation"/>, then always exits programming mode and releases the lock.
    /// The shared lock serializes installer and master operations against each other for the
    /// same session, preventing mode conflicts.
    /// </summary>
    Task<PanelAccessResult<T>> ExecuteAsync<T>(
        string sessionId,
        PanelAccessCodeKind kind,
        string code,
        bool readWrite,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct);

    /// <summary>
    /// Tests whether the panel accepts the given code. Implemented as <see cref="ExecuteAsync"/>
    /// with a no-op operation; on success, caches the code.
    /// </summary>
    Task<bool> VerifyAsync(string sessionId, PanelAccessCodeKind kind, string code, CancellationToken ct);
}

public class PanelAccessCodeService(
    IMediator mediator,
    IPanelStateService panelState,
    IOptionsMonitor<PanelConnectionsSettings> connectionSettings,
    ISettingsPersistenceService persistence,
    ILogger<PanelAccessCodeService> logger)
    : IPanelAccessCodeService
{
    /// <summary>How long to wait for the panel's programming-mode confirmation (LeadIn).</summary>
    private static readonly TimeSpan ProgrammingModeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Short settle delay after exiting programming mode before re-entering.</summary>
    private static readonly TimeSpan ReEntryDelay = TimeSpan.FromMilliseconds(500);

    private readonly ConcurrentDictionary<(string SessionId, PanelAccessCodeKind Kind), string> _cache
        = new(new CacheKeyComparer());

    // ── Format ───────────────────────────────────────────────────────────────

    public bool IsValidFormat(string? code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        if (code.Length != 4 && code.Length != 6 && code.Length != 8) return false;
        foreach (var ch in code)
            if (ch < '0' || ch > '9') return false;
        return true;
    }

    public string? ValidateFormat(string? code) =>
        IsValidFormat(code) ? null : "Access code must be 4, 6, or 8 digits";

    // ── Connection settings ──────────────────────────────────────────────────

    public string? GetConnectionCode(string sessionId, PanelAccessCodeKind kind)
    {
        var conn = FindConnection(sessionId);
        var value = conn is null ? null : kind switch
        {
            PanelAccessCodeKind.Installer => conn.InstallerCode,
            PanelAccessCodeKind.Master => conn.MasterCode,
            _ => null,
        };
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task<bool> UpdateConnectionCodeAsync(string sessionId, PanelAccessCodeKind kind, string? newCode)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;

        var conn = FindConnection(sessionId);
        if (conn is null) return false;

        var normalized = string.IsNullOrEmpty(newCode) ? null : newCode;

        var current = kind switch
        {
            PanelAccessCodeKind.Installer => conn.InstallerCode,
            PanelAccessCodeKind.Master => conn.MasterCode,
            _ => null,
        };
        if (current == normalized) return false;

        switch (kind)
        {
            case PanelAccessCodeKind.Installer: conn.InstallerCode = normalized; break;
            case PanelAccessCodeKind.Master: conn.MasterCode = normalized; break;
        }

        // Keep cache coherent with the persisted value.
        if (string.IsNullOrEmpty(normalized))
            InvalidateCache(sessionId, kind);
        else
            SetCached(sessionId, kind, normalized);

        await persistence.SaveSettingsAsync(typeof(PanelConnectionsSettings), connectionSettings.CurrentValue);
        logger.LogInformation(
            "Updated {Kind} code in connection settings for session {SessionId}", kind, sessionId);
        return true;
    }

    // ── Cache ────────────────────────────────────────────────────────────────

    public bool TryGetCached(string sessionId, PanelAccessCodeKind kind, out string? code)
    {
        if (_cache.TryGetValue((sessionId, kind), out var cached))
        {
            code = cached;
            return true;
        }
        code = null;
        return false;
    }

    public void SetCached(string sessionId, PanelAccessCodeKind kind, string? code)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(code))
        {
            InvalidateCache(sessionId, kind);
            return;
        }
        _cache[(sessionId, kind)] = code;
    }

    public void InvalidateCache(string sessionId, PanelAccessCodeKind kind) =>
        _cache.TryRemove((sessionId, kind), out _);

    // ── Authenticated operations ─────────────────────────────────────────────

    public async Task<PanelAccessResult<T>> ExecuteAsync<T>(
        string sessionId,
        PanelAccessCodeKind kind,
        string code,
        bool readWrite,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
            return new PanelAccessResult<T>(false, default, "Access code is required");

        var session = panelState.GetSession(sessionId);
        if (session is null)
            return new PanelAccessResult<T>(false, default, "Session not found");

        // Acquire the shared panel-mode lock for the full scope. This serializes all installer
        // and master operations for the same session — verify, reads, writes — so we never have
        // two callers fighting over the panel's single programming-mode slot.
        await session.ConfigLock.WaitAsync(ct);
        try
        {
            var mode = MapProgrammingMode(kind);

            if (!await EnterProgrammingModeAsync(sessionId, session, mode, code, readWrite, ct))
                return new PanelAccessResult<T>(false, default, $"Panel rejected {kind} code");

            try
            {
                var result = await operation(ct);
                return new PanelAccessResult<T>(true, result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Authenticated operation threw for {Kind} on session {SessionId}", kind, sessionId);
                return new PanelAccessResult<T>(false, default, ex.Message);
            }
            finally
            {
                await ExitProgrammingModeAsync(sessionId, ct);
            }
        }
        finally
        {
            session.ConfigLock.Release();
        }
    }

    public async Task<bool> VerifyAsync(string sessionId, PanelAccessCodeKind kind, string code, CancellationToken ct)
    {
        var result = await ExecuteAsync(
            sessionId, kind, code, readWrite: false,
            operation: _ => Task.FromResult(true),
            ct);

        if (result.Success)
            SetCached(sessionId, kind, code);

        return result.Success;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private ConnectionSettings? FindConnection(string? sessionId) =>
        string.IsNullOrEmpty(sessionId) ? null :
        connectionSettings.CurrentValue.Connections
            .FirstOrDefault(c => string.Equals(c.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

    private static ProgrammingMode MapProgrammingMode(PanelAccessCodeKind kind) => kind switch
    {
        PanelAccessCodeKind.Installer => ProgrammingMode.InstallersProgramming,
        PanelAccessCodeKind.Master => ProgrammingMode.AccessCodeProgramming,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Sends <see cref="ConfigurationEnter"/> and waits for the panel to confirm programming mode
    /// via the LeadIn notification. Exits first if the panel is already in programming mode (avoids
    /// the "partition busy" error).
    /// </summary>
    private async Task<bool> EnterProgrammingModeAsync(
        string sessionId, SessionState session, ProgrammingMode mode, string code, bool readWrite, CancellationToken ct)
    {
        if (session.IsInProgrammingMode)
        {
            logger.LogDebug("Panel already in programming mode on {SessionId}, exiting before re-enter", sessionId);
            await ExitProgrammingModeAsync(sessionId, ct);
            await Task.Delay(ReEntryDelay, ct);
        }

        logger.LogInformation("Entering {Mode} mode on {SessionId}", mode, sessionId);

        var enterResponse = await mediator.Send(new SessionCommand
        {
            SessionID = sessionId,
            MessageData = new ConfigurationEnter
            {
                Partition = 1,
                ProgrammingMode = mode,
                AccessCode = code,
                ReadWrite = readWrite
                    ? ConfigurationEnter.ReadWriteAccessEnum.ReadWriteMode
                    : ConfigurationEnter.ReadWriteAccessEnum.ReadOnlyMode
            }
        }, ct);

        if (!enterResponse.Success)
        {
            logger.LogWarning("Failed to enter {Mode} on {SessionId}: {Error}",
                mode, sessionId, enterResponse.ErrorMessage);
            return false;
        }

        // Wait for LeadIn. Without this, the panel sometimes rejects the first real command
        // sent inside the scope with a spurious "not ready" error.
        var deadline = DateTime.UtcNow + ProgrammingModeTimeout;
        while (!session.IsInProgrammingMode && DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(100, ct);
        }

        if (!session.IsInProgrammingMode)
        {
            logger.LogWarning("Timed out waiting for LeadIn after {Seconds}s on {SessionId}",
                ProgrammingModeTimeout.TotalSeconds, sessionId);
            await ExitProgrammingModeAsync(sessionId, ct);
            return false;
        }

        logger.LogDebug("Panel confirmed {Mode} on {SessionId}", mode, sessionId);
        return true;
    }

    /// <summary>Best-effort exit. Swallows and logs any failure since we never want cleanup to throw.</summary>
    private async Task ExitProgrammingModeAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            await mediator.Send(new SessionCommand
            {
                SessionID = sessionId,
                MessageData = new ConfigurationExit { Partition = 1 }
            }, ct);
            logger.LogDebug("Exited programming mode on {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to exit programming mode on {SessionId}", sessionId);
        }
    }

    /// <summary>Case-insensitive equality for the session-id portion of the cache key.</summary>
    private sealed class CacheKeyComparer : IEqualityComparer<(string SessionId, PanelAccessCodeKind Kind)>
    {
        public bool Equals((string SessionId, PanelAccessCodeKind Kind) x, (string SessionId, PanelAccessCodeKind Kind) y)
            => x.Kind == y.Kind && string.Equals(x.SessionId, y.SessionId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string SessionId, PanelAccessCodeKind Kind) obj)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SessionId), obj.Kind);
    }
}
