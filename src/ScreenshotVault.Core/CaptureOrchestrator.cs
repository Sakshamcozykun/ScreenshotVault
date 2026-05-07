// src/ScreenshotVault.Core/CaptureOrchestrator.cs
using ScreenshotVault.Core.Capture;
using ScreenshotVault.Core.Classification;
using ScreenshotVault.Core.Models;
using ScreenshotVault.Core.Storage;

namespace ScreenshotVault.Core;

/// <summary>
/// Wires together: Hook → Context → Classify → Save → Notify.
/// This is the single entry point the UI layer instantiates.
/// </summary>
public sealed class CaptureOrchestrator : IDisposable
{
    private readonly GlobalHookService    _hook;
    private readonly ScreenCaptureService _capture;
    private readonly ContextExtractor     _context;
    private readonly RulesEngine          _rules;
    private readonly ActiveLearner        _learner;
    private readonly ScreenshotRepository _repo;

    public event EventHandler<ScreenshotSavedEventArgs>? ScreenshotSaved;

    public CaptureOrchestrator(
        GlobalHookService    hook,
        ScreenCaptureService capture,
        ContextExtractor     context,
        RulesEngine          rules,
        ActiveLearner        learner,
        ScreenshotRepository repo)
    {
        _hook    = hook;
        _capture = capture;
        _context = context;
        _rules   = rules;
        _learner = learner;
        _repo    = repo;

        _hook.ScreenshotCaptured += OnScreenshotCaptured;
    }

    public void Start() => _hook.Install();

    private async void OnScreenshotCaptured(object? sender, CaptureEventArgs e)
    {
        try
        {
            var category = _rules.Classify(e.Context);
            var id       = await _repo.SaveAsync(e.ImageBytes, e.Context, category);

            ScreenshotSaved?.Invoke(this,
                new ScreenshotSavedEventArgs(id, category, e.Context));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Orchestrator] Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by the UI when the user manually reclassifies a Misc screenshot.
    /// Triggers the active learning update cycle.
    /// </summary>
    public async Task ReclassifyAsync(Guid screenshotId, WindowContext context,
        string newCategory)
    {
        await _repo.UpdateCategoryAsync(screenshotId, newCategory);
        _learner.RecordCorrection(context, newCategory); // Updates weights + persists rules
    }

    public void Dispose()
    {
        _hook.ScreenshotCaptured -= OnScreenshotCaptured;
        _hook.Dispose();
    }
}

public sealed class ScreenshotSavedEventArgs : EventArgs
{
    public Guid          Id       { get; }
    public string        Category { get; }
    public WindowContext Context  { get; }

    public ScreenshotSavedEventArgs(Guid id, string category, WindowContext ctx)
    {
        Id = id; Category = category; Context = ctx;
    }
}
