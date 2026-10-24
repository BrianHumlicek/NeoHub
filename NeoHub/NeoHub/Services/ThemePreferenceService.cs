namespace NeoHub.Services;

public class ThemePreferenceService
{
    private string _preference = "system";

    public string Preference => _preference;

    public event Action? ThemeChanged;

    public void SetPreference(string preference)
    {
        if (preference is not ("light" or "dark" or "system"))
            return;

        if (_preference == preference)
            return;

        _preference = preference;
        ThemeChanged?.Invoke();
    }
}
