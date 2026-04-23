using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using NeoHub.Components.Pages;

namespace NeoHub.Services;

/// <summary>
/// Razor-facing conveniences that wire <see cref="AccessCodePromptDialog"/> to
/// <see cref="IPanelAccessCodeService"/>. Lives outside the service so the service stays
/// UI-free (testable in plain unit tests with no MudBlazor dependencies).
/// </summary>
public static class AccessCodeDialogExtensions
{
    /// <summary>
    /// Shows <see cref="AccessCodePromptDialog"/> pre-wired with the appropriate label, validator,
    /// and panel-side verifier for the given code kind. Returns the verified code on success, or
    /// null if the operator cancels.
    /// </summary>
    /// <remarks>
    /// On success, the code is cached via <see cref="IPanelAccessCodeService.SetCached"/> so
    /// subsequent calls in the same session avoid re-prompting. The code is NOT automatically
    /// persisted to connection settings — call <see cref="IPanelAccessCodeService.UpdateConnectionCodeAsync"/>
    /// explicitly if desired.
    /// </remarks>
    public static async Task<string?> PromptForAccessCodeAsync(
        this IDialogService dialogs,
        IPanelAccessCodeService accessCodes,
        string sessionId,
        PanelAccessCodeKind kind,
        CancellationToken ct = default)
    {
        var parameters = new DialogParameters<AccessCodePromptDialog>
        {
            { x => x.Label, LabelFor(kind) },
            { x => x.HelperText, HelperTextFor(kind) },
            { x => x.MaxLength, 8 },
            { x => x.InputMode, InputMode.numeric },
            { x => x.Validator, (string? c) => accessCodes.ValidateFormat(c) },
            { x => x.VerifyAsync, (Func<string, CancellationToken, Task<bool>>)((code, token) =>
                accessCodes.VerifyAsync(sessionId, kind, code, token)) },
            { x => x.VerificationFailedMessage, $"Incorrect {LabelFor(kind).ToLower()}. Please try again." }
        };

        var options = new DialogOptions
        {
            CloseButton = true,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        };

        var dialog = await dialogs.ShowAsync<AccessCodePromptDialog>(
            $"Enter {LabelFor(kind)}", parameters, options);
        var result = await dialog.Result;

        if (result?.Canceled != false)
            return null;

        var code = result.Data as string;
        if (!string.IsNullOrEmpty(code))
            accessCodes.SetCached(sessionId, kind, code);
        return code;
    }

    /// <summary>
    /// Full resolve flow for a panel access code:
    /// <list type="number">
    /// <item>Return cached value if present</item>
    /// <item>Return configured value from <c>ConnectionSettings</c> if set (also caches it)</item>
    /// <item>Otherwise prompt via <see cref="PromptForAccessCodeAsync"/></item>
    /// </list>
    /// Returns the verified code, or null if no value was available and the operator cancelled.
    /// </summary>
    public static async Task<string?> ResolveAccessCodeAsync(
        this IDialogService dialogs,
        IPanelAccessCodeService accessCodes,
        string sessionId,
        PanelAccessCodeKind kind,
        CancellationToken ct = default)
    {
        if (accessCodes.TryGetCached(sessionId, kind, out var cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var configured = accessCodes.GetConnectionCode(sessionId, kind);
        if (!string.IsNullOrEmpty(configured))
        {
            // Trust the configured value without a verify round-trip. If it's wrong, the first
            // real operation will fail with an auth error and the caller can invalidate + re-prompt.
            accessCodes.SetCached(sessionId, kind, configured);
            return configured;
        }

        return await dialogs.PromptForAccessCodeAsync(accessCodes, sessionId, kind, ct);
    }

    private static string LabelFor(PanelAccessCodeKind kind) => kind switch
    {
        PanelAccessCodeKind.Installer => "Installer Code",
        PanelAccessCodeKind.Master => "Master Code",
        _ => "Access Code",
    };

    private static string HelperTextFor(PanelAccessCodeKind kind) => kind switch
    {
        PanelAccessCodeKind.Installer => "Enter the panel's installer code (4, 6, or 8 digits) to read or write configuration",
        PanelAccessCodeKind.Master => "Enter the panel's master code (4, 6, or 8 digits) to manage user access codes",
        _ => "4, 6, or 8 digits",
    };
}
