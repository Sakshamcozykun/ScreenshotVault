// src/ScreenshotVault.App/ViewModels/SwipeModeViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenshotVault.Core.Models;
using ScreenshotVault.Core.Storage;

namespace ScreenshotVault.App.ViewModels;

public enum SwipeDirection { None, Left, Right }
public enum SwipeAction    { Delete, Keep }

public sealed partial class SwipeModeViewModel : ObservableObject
{
    private readonly ScreenshotRepository _repo;

    private List<Screenshot> _queue  = [];
    private int              _cursor = 0;
    private const int        MaxUndo = 20;

    // ── Observable state ────────────────────────────────────────────────────
    [ObservableProperty] private Screenshot?      _current;
    [ObservableProperty] private int              _remaining;
    [ObservableProperty] private int              _totalInSession;
    [ObservableProperty] private int              _deletedCount;
    [ObservableProperty] private int              _keptCount;
    [ObservableProperty] private SwipeDirection   _lastSwipe = SwipeDirection.None;
    [ObservableProperty] private bool             _isComplete;
    [ObservableProperty] private bool             _canUndo;
    [ObservableProperty] private string           _sessionFilter = "All";

    // Undo stack: bounded at MaxUndo entries
    private readonly LinkedList<(Screenshot Item, SwipeAction Action)> _undoStack = new();

    public SwipeModeViewModel(ScreenshotRepository repo)
    {
        _repo = repo;
    }

    public async Task LoadAsync(string? categoryFilter = null)
    {
        SessionFilter  = categoryFilter ?? "All";
        IsComplete     = false;
        DeletedCount   = 0;
        KeptCount      = 0;
        _undoStack.Clear();
        CanUndo        = false;

        _queue = categoryFilter is null
            ? await _repo.GetAllAsync()
            : await _repo.GetByCategoryAsync(categoryFilter);

        TotalInSession = _queue.Count;
        _cursor        = 0;
        Advance(rewind: false);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SwipeLeftAsync()   // ← Delete
    {
        if (Current is null) return;
        LastSwipe = SwipeDirection.Left;
        PushUndo(Current, SwipeAction.Delete);

        await _repo.SoftDeleteAsync(Current.Id);
        DeletedCount++;
        Advance(rewind: false);
    }

    [RelayCommand]
    private async Task SwipeRightAsync()  // → Keep
    {
        if (Current is null) return;
        LastSwipe = SwipeDirection.Right;
        PushUndo(Current, SwipeAction.Keep);

        await _repo.MarkKeptAsync(Current.Id);
        KeptCount++;
        Advance(rewind: false);
    }

    [RelayCommand]
    private async Task UndoLastAsync()
    {
        if (_undoStack.Count == 0) return;

        var (item, action) = _undoStack.First!.Value;
        _undoStack.RemoveFirst();
        CanUndo = _undoStack.Count > 0;

        if (action == SwipeAction.Delete)
            await _repo.RestoreAsync(item.Id);
        // Keep action has no file side-effect to undo (just DB flag)

        // Re-insert item at current position and rewind
        if (_cursor > 0) _cursor--;
        _queue.Insert(_cursor, item);

        if (action == SwipeAction.Delete) DeletedCount = Math.Max(0, DeletedCount - 1);
        else                              KeptCount    = Math.Max(0, KeptCount    - 1);

        Advance(rewind: true);
    }

    /// <summary>Keyboard handler — bind to KeyDown on SwipeModeView.</summary>
    public void HandleKey(Windows.System.VirtualKey key, bool ctrlHeld)
    {
        switch (key)
        {
            case Windows.System.VirtualKey.Left:
                if (SwipeLeftCommand.CanExecute(null))
                    SwipeLeftCommand.Execute(null);
                break;
            case Windows.System.VirtualKey.Right:
                if (SwipeRightCommand.CanExecute(null))
                    SwipeRightCommand.Execute(null);
                break;
            case Windows.System.VirtualKey.Z when ctrlHeld:
                if (UndoLastCommand.CanExecute(null))
                    UndoLastCommand.Execute(null);
                break;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void Advance(bool rewind)
    {
        if (!rewind) _cursor++;

        if (_cursor >= _queue.Count)
        {
            Current    = null;
            IsComplete = true;
            Remaining  = 0;
            return;
        }

        Current    = _queue[_cursor];
        IsComplete = false;
        Remaining  = _queue.Count - _cursor;
    }

    private void PushUndo(Screenshot item, SwipeAction action)
    {
        _undoStack.AddFirst((item, action));
        if (_undoStack.Count > MaxUndo) _undoStack.RemoveLast();
        CanUndo = true;
    }
}
