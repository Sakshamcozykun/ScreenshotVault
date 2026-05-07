// ============================================================
// src/ScreenshotVault.Core/Models/Screenshot.cs
// ============================================================
namespace ScreenshotVault.Core.Models;

public sealed class Screenshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FilePath { get; set; } = "";
    public string Category { get; set; } = "Miscellaneous";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public bool IsSoftDeleted { get; set; }
    public bool IsKept { get; set; }
    public WindowContext Context { get; set; } = new();
}

// ============================================================
// src/ScreenshotVault.Core/Models/WindowContext.cs
// ============================================================
namespace ScreenshotVault.Core.Models;

public sealed class WindowContext
{
    public string? ProcessName { get; set; }
    public string? WindowTitle { get; set; }
    public string? Url { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public static WindowContext Unknown => new()
    {
        ProcessName = "unknown",
        WindowTitle = "Unknown",
        Url = null
    };
}

// ============================================================
// src/ScreenshotVault.Core/Models/Category.cs
// ============================================================
namespace ScreenshotVault.Core.Models;

public sealed class Category
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public bool IsSystem { get; set; }      // Cannot be deleted by user
    public int ScreenshotCount { get; set; }
}
