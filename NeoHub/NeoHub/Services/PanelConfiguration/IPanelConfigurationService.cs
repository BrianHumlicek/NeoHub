using DSC.TLink.ITv2.Messages;

namespace NeoHub.Services.PanelConfiguration;

/// <summary>
/// Constrained delegate for sending a SectionRead to the panel.
/// Returns null on failure — the caller only needs the response data, not an error message.
/// </summary>
public delegate Task<SectionReadResponse?> SendSectionRead(SectionRead request, CancellationToken ct);

/// <summary>
/// Constrained delegate for sending a SectionWrite to the panel.
/// </summary>
public delegate Task<SectionResult> SendSectionWrite(SectionWrite request, CancellationToken ct);

/// <summary>
/// Simple success/failure result for panel configuration operations.
/// Used by both service-level operations (ReadAllAsync) and individual section writes.
/// </summary>
public record SectionResult(bool Success, string? ErrorMessage = null);

public interface IPanelConfigurationService
{
    /// <summary>
    /// Acquires the config lock, enters installer config mode, reads all known sections,
    /// exits config mode, and releases the lock.
    /// Results are stored on SessionState.Configuration.
    /// </summary>
    Task<SectionResult> ReadAllAsync(string sessionId, string installerCode, CancellationToken ct);

    /// <summary>
    /// Quickly tests whether the panel accepts <paramref name="installerCode"/> by entering
    /// and immediately exiting installer programming mode. Returns true on success.
    /// Used by the operator prompt dialog to validate the code before any real read/write.
    /// </summary>
    Task<bool> VerifyInstallerCodeAsync(string sessionId, string installerCode, CancellationToken ct);

    /// <summary>
    /// Reads all configuration sections using the provided send delegate.
    /// Does NOT manage config mode entry/exit or acquire the config lock —
    /// the caller is responsible for both.
    /// </summary>
    Task ReadSectionsAsync(string sessionId, SendSectionRead send, CancellationToken ct);

    /// <summary>
    /// Enters config mode with the specified privilege, executes the operation,
    /// then always exits. Does NOT acquire the config lock — the caller must
    /// hold it if concurrent access is a concern.
    /// </summary>
    Task<SectionResult> ExecuteInConfigModeAsync(
        string sessionId, string installerCode, bool readWrite,
        Func<Task<SectionResult>> operation, CancellationToken ct);

    /// <summary>
    /// Creates a <see cref="SendSectionWrite"/> delegate bound to the specified session.
    /// The delegate sends a <see cref="SectionWrite"/> via the mediator and returns the result.
    /// Must be called within an active config mode session.
    /// </summary>
    SendSectionWrite CreateWriteDelegate(string sessionId);
}
