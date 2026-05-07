// src/ScreenshotVault.App/Views/MainShell.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenshotVault.App.Services;
using ScreenshotVault.Core;

namespace ScreenshotVault.App.Views;

public sealed partial class MainShell : Window
{
    private readonly ThemeService        _theme;
    private readonly CaptureOrchestrator _orchestrator;

    public MainShell(ThemeService theme, CaptureOrchestrator orchestrator)
    {
        InitializeComponent();
        _theme        = theme;
        _orchestrator = orchestrator;

        _theme.ThemeChanged      += OnThemeChanged;
        _orchestrator.ScreenshotSaved += OnScreenshotSaved;

        // Load saved theme preference
        _theme.LoadSavedTheme();

        // Start hook after window is created
        _orchestrator.Start();

        // Navigate to gallery on startup
        NavigateTo(typeof(GalleryView));
    }

    private void OnNavGallery(object sender, RoutedEventArgs e)
        => NavigateTo(typeof(GalleryView));

    private void OnNavSwipe(object sender, RoutedEventArgs e)
        => NavigateTo(typeof(SwipeModeView));

    private void OnNavMisc(object sender, RoutedEventArgs e)
        => NavigateTo(typeof(MiscClassifyView));

    private void OnToggleTheme(object sender, RoutedEventArgs e)
        => _theme.Toggle();

    private void NavigateTo(Type pageType)
    {
        MainFrame.Navigate(pageType);
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        // TxtThemeLabel text is auto-updated via ThemeResource binding.
        // Force a status bar refresh for immediate visual feedback.
        TxtStatus.Text = $"Theme switched to {theme}";
    }

    private void OnScreenshotSaved(object? sender, ScreenshotSavedEventArgs e)
    {
        // Marshal to UI thread — event fires from background Task
        DispatcherQueue.TryEnqueue(() =>
        {
            ShowToast(
                "Screenshot captured",
                $"Saved to: {e.Category}  •  {e.Context.WindowTitle ?? e.Context.ProcessName}");

            TxtStatus.Text = $"Last capture → {e.Category} at {DateTime.Now:HH:mm:ss}";
        });
    }

    private async void ShowToast(string title, string subtitle)
    {
        ToastTitle.Text    = title;
        ToastSubtitle.Text = subtitle;
        ToastBanner.Visibility = Visibility.Visible;

        await Task.Delay(3000);

        ToastBanner.Visibility = Visibility.Collapsed;
    }
}
