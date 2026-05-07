// src/ScreenshotVault.App/Views/GalleryView.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenshotVault.App.ViewModels;
using ScreenshotVault.Core.Models;

namespace ScreenshotVault.App.Views;

public sealed partial class GalleryView : Page
{
    public GalleryViewModel ViewModel { get; }

    public GalleryView(GalleryViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        _ = ViewModel.LoadAsync();
    }

    private void OnFilterAll(object sender, RoutedEventArgs e)
        => _ = ViewModel.FilterByCategoryAsync("All");

    private void OnFilterCategory(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string category)
            _ = ViewModel.FilterByCategoryAsync(category);
    }

    private async void OnDeleteCategory(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string category) return;

        // Confirm before bulk delete
        var dialog = new ContentDialog
        {
            Title             = $"Delete all in '{category}'?",
            Content           = "This will permanently remove all screenshots in this category.",
            PrimaryButtonText = "Delete All",
            CloseButtonText   = "Cancel",
            XamlRoot          = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            _ = ViewModel.DeleteCategoryAsync(category);
    }

    private void OnScreenshotClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Screenshot shot)
            ViewModel.OpenDetailCommand.Execute(shot);
    }

    private void OnCloseDetail(object sender, RoutedEventArgs e)
        => ViewModel.CloseDetailCommand.Execute(null);
}
