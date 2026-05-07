// src/ScreenshotVault.Core/Storage/MetadataStore.cs
using Microsoft.EntityFrameworkCore;
using ScreenshotVault.Core.Models;
using System.Text.Json;

namespace ScreenshotVault.Core.Storage;

/// <summary>EF Core DbContext backed by SQLite at %LocalAppData%\ScreenshotVault\vault.db</summary>
public sealed class VaultDbContext : DbContext
{
    private readonly string _dbPath;

    public VaultDbContext(string dbPath) { _dbPath = dbPath; }

    public DbSet<ScreenshotEntity> Screenshots => Set<ScreenshotEntity>();
    public DbSet<CategoryEntity>   Categories  => Set<CategoryEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder opts)
        => opts.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ScreenshotEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ContextJson).HasColumnType("TEXT");
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.IsSoftDeleted);
        });
        model.Entity<CategoryEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });
    }
}

// Flat EF entities (no nested objects in SQLite columns)
public sealed class ScreenshotEntity
{
    public Guid     Id            { get; set; } = Guid.NewGuid();
    public string   FilePath      { get; set; } = "";
    public string   Category      { get; set; } = "Miscellaneous";
    public DateTime CapturedAt    { get; set; } = DateTime.UtcNow;
    public bool     IsSoftDeleted { get; set; }
    public bool     IsKept        { get; set; }
    public string   ContextJson   { get; set; } = "{}";  // Serialised WindowContext
}

public sealed class CategoryEntity
{
    public Guid   Id         { get; set; } = Guid.NewGuid();
    public string Name       { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public bool   IsSystem   { get; set; }
}

// ============================================================
// src/ScreenshotVault.Core/Storage/ScreenshotRepository.cs
// ============================================================
namespace ScreenshotVault.Core.Storage;

public sealed class ScreenshotRepository
{
    private readonly Func<VaultDbContext> _dbFactory;
    private readonly string _screenshotsRoot;

    public ScreenshotRepository(Func<VaultDbContext> dbFactory, string screenshotsRoot)
    {
        _dbFactory       = dbFactory;
        _screenshotsRoot = screenshotsRoot;
    }

    public async Task EnsureSchemaAsync()
    {
        using var db = _dbFactory();
        await db.Database.EnsureCreatedAsync();
        EnsureSystemCategories(db);
    }

    private static void EnsureSystemCategories(VaultDbContext db)
    {
        string[] systemCategories = ["Miscellaneous", "Work", "Development", "Browser"];
        foreach (var name in systemCategories)
        {
            if (!db.Categories.Any(c => c.Name == name))
                db.Categories.Add(new CategoryEntity { Name = name, IsSystem = true,
                    FolderPath = name });
        }
        db.SaveChanges();
    }

    public async Task<Guid> SaveAsync(byte[] imageBytes, WindowContext context, string category)
    {
        // Ensure category folder exists
        var folderPath = Path.Combine(_screenshotsRoot, category);
        Directory.CreateDirectory(folderPath);

        var fileName  = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var filePath  = Path.Combine(folderPath, fileName);
        await File.WriteAllBytesAsync(filePath, imageBytes);

        var entity = new ScreenshotEntity
        {
            FilePath    = filePath,
            Category    = category,
            CapturedAt  = DateTime.UtcNow,
            ContextJson = JsonSerializer.Serialize(context),
        };

        using var db = _dbFactory();
        db.Screenshots.Add(entity);
        await db.SaveChangesAsync();

        return entity.Id;
    }

    public async Task<List<Screenshot>> GetByCategoryAsync(string category)
    {
        using var db = _dbFactory();
        var entities = await db.Screenshots
            .Where(s => s.Category == category && !s.IsSoftDeleted)
            .OrderByDescending(s => s.CapturedAt)
            .ToListAsync();
        return entities.Select(Map).ToList();
    }

    public async Task<List<Screenshot>> GetAllAsync()
    {
        using var db = _dbFactory();
        var entities = await db.Screenshots
            .Where(s => !s.IsSoftDeleted)
            .OrderByDescending(s => s.CapturedAt)
            .ToListAsync();
        return entities.Select(Map).ToList();
    }

    public async Task SoftDeleteAsync(Guid id)
    {
        using var db = _dbFactory();
        var entity = await db.Screenshots.FindAsync(id);
        if (entity == null) return;

        entity.IsSoftDeleted = true;

        // Move file to .trash subfolder
        var trashDir  = Path.Combine(_screenshotsRoot, ".trash");
        Directory.CreateDirectory(trashDir);
        var trashPath = Path.Combine(trashDir, Path.GetFileName(entity.FilePath));
        if (File.Exists(entity.FilePath))
            File.Move(entity.FilePath, trashPath, overwrite: true);
        entity.FilePath = trashPath;

        await db.SaveChangesAsync();
    }

    public async Task RestoreAsync(Guid id)
    {
        using var db = _dbFactory();
        var entity = await db.Screenshots.FindAsync(id);
        if (entity == null) return;

        entity.IsSoftDeleted = false;

        // Move file back from .trash
        var categoryDir  = Path.Combine(_screenshotsRoot, entity.Category);
        Directory.CreateDirectory(categoryDir);
        var restoredPath = Path.Combine(categoryDir, Path.GetFileName(entity.FilePath));
        if (File.Exists(entity.FilePath))
            File.Move(entity.FilePath, restoredPath, overwrite: true);
        entity.FilePath = restoredPath;

        await db.SaveChangesAsync();
    }

    public async Task MarkKeptAsync(Guid id)
    {
        using var db = _dbFactory();
        var entity = await db.Screenshots.FindAsync(id);
        if (entity == null) return;
        entity.IsKept = true;
        await db.SaveChangesAsync();
    }

    public async Task UpdateCategoryAsync(Guid id, string newCategory)
    {
        using var db = _dbFactory();
        var entity = await db.Screenshots.FindAsync(id);
        if (entity == null) return;

        // Move file to new category folder
        var newDir  = Path.Combine(_screenshotsRoot, newCategory);
        Directory.CreateDirectory(newDir);
        var newPath = Path.Combine(newDir, Path.GetFileName(entity.FilePath));
        if (File.Exists(entity.FilePath) && entity.FilePath != newPath)
            File.Move(entity.FilePath, newPath, overwrite: true);

        entity.Category = newCategory;
        entity.FilePath = newPath;
        await db.SaveChangesAsync();
    }

    public async Task DeleteCategoryBulkAsync(string category)
    {
        using var db = _dbFactory();
        var entities = await db.Screenshots
            .Where(s => s.Category == category)
            .ToListAsync();

        foreach (var e in entities)
        {
            if (File.Exists(e.FilePath)) File.Delete(e.FilePath);
        }

        db.Screenshots.RemoveRange(entities);
        await db.SaveChangesAsync();
    }

    public async Task<List<Category>> GetCategorySummaryAsync()
    {
        using var db = _dbFactory();
        var counts = await db.Screenshots
            .Where(s => !s.IsSoftDeleted)
            .GroupBy(s => s.Category)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        var categories = await db.Categories.ToListAsync();

        return categories.Select(c => new Category
        {
            Id              = c.Id,
            Name            = c.Name,
            FolderPath      = c.FolderPath,
            IsSystem        = c.IsSystem,
            ScreenshotCount = counts.FirstOrDefault(x => x.Key == c.Name)?.Count ?? 0,
        }).ToList();
    }

    private static Screenshot Map(ScreenshotEntity e) => new()
    {
        Id            = e.Id,
        FilePath      = e.FilePath,
        Category      = e.Category,
        CapturedAt    = e.CapturedAt,
        IsSoftDeleted = e.IsSoftDeleted,
        IsKept        = e.IsKept,
        Context       = JsonSerializer.Deserialize<WindowContext>(e.ContextJson)
                        ?? WindowContext.Unknown,
    };
}
