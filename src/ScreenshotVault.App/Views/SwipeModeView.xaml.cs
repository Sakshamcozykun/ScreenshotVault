// src/ScreenshotVault.App/Views/SwipeModeView.xaml.cs
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScreenshotVault.App.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace ScreenshotVault.App.Views;

public sealed partial class SwipeModeView : Page
{
    public SwipeModeViewModel ViewModel { get; }

    public SwipeModeView(SwipeModeViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        _ = ViewModel.LoadAsync(); // Load all screenshots on entry
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrlHeld = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        ViewModel.HandleKey(e.Key, ctrlHeld);
        e.Handled = true;
    }
}
