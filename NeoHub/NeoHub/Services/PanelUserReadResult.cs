namespace NeoHub.Services
{
    /// <summary>
    /// Outcome of <see cref="IPanelUserService.ReadAllAsync"/>. Reads are all-or-nothing:
    /// success means every user slot was read and committed to session state; failure means
    /// nothing was committed and any prior data on the session is preserved.
    /// </summary>
    public record PanelUserReadResult(bool Success, string? ErrorMessage = null);
}
