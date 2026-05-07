// src/ScreenshotVault.App/Services/ThemeService.cs
using Microsoft.UI.Xaml;

namespace ScreenshotVault.App.Services;

public enum AppTheme { Modern, XPPixelArt }

/// <summary>
/// Swaps the active ResourceDictionary theme at runtime — no restart required.
/// Both theme files pre-define the same resource keys so all controls re-render
/// automatically when the dictionary is replaced.
/// </summary>
public sealed class ThemeService
{
    private static readonly Uri ModernThemeUri =
        new("ms-appx:///Assets/Themes/Modern.xaml");
    private static readonly Uri XPThemeUri =
        new("ms-appx:///Assets/Themes/XPPixelArt.xaml");

    private AppTheme _current = AppTheme.Modern;
    public AppTheme Current => _current;

    public event EventHandler<AppTheme>? ThemeChanged;

    public void Apply(AppTheme theme)
    {
        var targetUri = theme == AppTheme.XPPixelArt ? XPThemeUri : ModernThemeUri;
        SwapThemeDictionary(targetUri);
        _current = theme;
        ThemeChanged?.Invoke(this, theme);
        PersistThemePreference(theme);
    }

    public void Toggle() =>
        Apply(_current == AppTheme.Modern ? AppTheme.XPPixelArt : AppTheme.Modern);

    public void LoadSavedTheme()
    {
        var saved = LoadThemePreference();
        Apply(saved);
    }

    private static void SwapThemeDictionary(Uri targetUri)
    {
        var merged = Application.Current.Resources.MergedDictionaries;

        // Find the currently active theme dictionary
        var existing = merged.FirstOrDefault(d =>
            d.Source == ModernThemeUri || d.Source == XPThemeUri);

        if (existing?.Source == targetUri) return; // Already active

        if (existing != null) merged.Remove(existing);
        merged.Add(new ResourceDictionary { Source = targetUri });
    }

    private static void PersistThemePreference(AppTheme theme)
    {
        try
        {
            var settingsPath = GetSettingsPath();
            File.WriteAllText(settingsPath, theme.ToString());
        }
        catch { /* Non-critical */ }
    }

    private static AppTheme LoadThemePreference()
    {
        try
        {
            var settingsPath = GetSettingsPath();
            if (File.Exists(settingsPath))
            {
                var raw = File.ReadAllText(settingsPath).Trim();
                if (Enum.TryParse<AppTheme>(raw, out var parsed)) return parsed;
            }
        }
        catch { }
        return AppTheme.Modern;
    }

    private static string GetSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenshotVault", "theme.txt");
}
