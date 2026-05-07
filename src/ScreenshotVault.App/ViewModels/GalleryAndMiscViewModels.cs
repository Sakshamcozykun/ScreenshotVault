// src/ScreenshotVault.App/ViewModels/GalleryViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenshotVault.Core.Models;
using ScreenshotVault.Core.Storage;

namespace ScreenshotVault.App.ViewModels;

public sealed partial class GalleryViewModel : ObservableObject
{
    private readonly ScreenshotRepository _repo;

    [ObservableProperty] private List<Screenshot>    _screenshots = [];
    [ObservableProperty] private List<Category>      _categories  = [];
    [ObservableProperty] private string              _activeCategory = "All";
    [ObservableProperty] private Screenshot?         _selectedScreenshot;
    [ObservableProperty] private bool                _isDetailOpen;

    public GalleryViewModel(ScreenshotRepository repo) { _repo = repo; }

    public async Task LoadAsync()
    {
        Categories = await _repo.GetCategorySummaryAsync();
        await FilterByCategoryAsync(ActiveCategory);
    }

    [RelayCommand]
    public async Task FilterByCategoryAsync(string category)
    {
        ActiveCategory = category;
        Screenshots = category == "All"
            ? await _repo.GetAllAsync()
            : await _repo.GetByCategoryAsync(category);
    }

    [RelayCommand]
    private void OpenDetail(Screenshot shot)
    {
        SelectedScreenshot = shot;
        IsDetailOpen       = true;
    }

    [RelayCommand]
    private void CloseDetail() { IsDetailOpen = false; }

    /// <summary>Bulk delete: removes all screenshots in a given category.</summary>
    [RelayCommand]
    private async Task DeleteCategoryAsync(string category)
    {
        await _repo.DeleteCategoryBulkAsync(category);
        await LoadAsync();
    }

    /// <summary>Called when new screenshot is captured — refreshes the active view.</summary>
    public async Task OnNewScreenshotAsync()
        => await FilterByCategoryAsync(ActiveCategory);
}

// ============================================================
// src/ScreenshotVault.App/ViewModels/MiscClassifyViewModel.cs
// ============================================================
using ScreenshotVault.Core;

namespace ScreenshotVault.App.ViewModels;

/// <summary>
/// Powers the Miscellaneous triage view.
/// Each classification by the user feeds back to the ActiveLearner.
/// </summary>
public sealed partial class MiscClassifyViewModel : ObservableObject
{
    private readonly ScreenshotRepository _repo;
    private readonly CaptureOrchestrator  _orchestrator;

    [ObservableProperty] private List<Screenshot> _miscScreenshots = [];
    [ObservableProperty] private List<string>     _availableCategories = [];
    [ObservableProperty] private int              _classifiedCount;

    public MiscClassifyViewModel(ScreenshotRepository repo, CaptureOrchestrator orchestrator)
    {
        _repo         = repo;
        _orchestrator = orchestrator;
    }

    public async Task LoadAsync()
    {
        MiscScreenshots = await _repo.GetByCategoryAsync("Miscellaneous");
        var summary     = await _repo.GetCategorySummaryAsync();
        AvailableCategories = summary
            .Where(c => c.Name != "Miscellaneous")
            .Select(c => c.Name)
            .ToList();
    }

    [RelayCommand]
    private async Task ClassifyAsync(ClassifyRequest req)
    {
        await _orchestrator.ReclassifyAsync(
            req.Screenshot.Id,
            req.Screenshot.Context,
            req.TargetCategory);

        // Remove from local list immediately for responsive UI
        MiscScreenshots = MiscScreenshots
            .Where(s => s.Id != req.Screenshot.Id)
            .ToList();

        ClassifiedCount++;
    }

    [RelayCommand]
    private async Task ClassifyAllAsync(string targetCategory)
    {
        foreach (var shot in MiscScreenshots.ToList())
            await ClassifyAsync(new ClassifyRequest(shot, targetCategory));
    }
}

public record ClassifyRequest(Screenshot Screenshot, string TargetCategory);
