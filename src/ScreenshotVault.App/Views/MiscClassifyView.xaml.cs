// src/ScreenshotVault.App/Views/MiscClassifyView.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenshotVault.App.ViewModels;
using ScreenshotVault.Core.Models;

namespace ScreenshotVault.App.Views;

public sealed partial class MiscClassifyView : Page
{
    public MiscClassifyViewModel ViewModel { get; }

    public MiscClassifyView(MiscClassifyViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        _ = ViewModel.LoadAsync();
    }

    private void OnClassifyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not Screenshot shot) return;

        // Walk up visual tree to find the ComboBox in the same DataTemplate row
        var panel   = btn.Parent as StackPanel;
        var picker  = panel?.Children.OfType<ComboBox>().FirstOrDefault();
        var selected = picker?.SelectedItem as string;

        if (string.IsNullOrEmpty(selected))
        {
            // Show inline validation — no dialog needed
            if (picker != null) picker.PlaceholderText = "⚠ Pick a category first";
            return;
        }

        ViewModel.ClassifyCommand.Execute(new ClassifyRequest(shot, selected));
    }
}
