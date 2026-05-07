// src/ScreenshotVault.App/App.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ScreenshotVault.App.Services;
using ScreenshotVault.App.ViewModels;
using ScreenshotVault.App.Views;
using ScreenshotVault.Core;
using ScreenshotVault.Core.Capture;
using ScreenshotVault.Core.Classification;
using ScreenshotVault.Core.Storage;

namespace ScreenshotVault.App;

public partial class App : Application
{
    private static readonly string AppDataRoot =
        Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "ScreenshotVault");

    private static readonly string ScreenshotsRoot = Path.Combine(AppDataRoot, "screenshots");
    private static readonly string DbPath          = Path.Combine(AppDataRoot, "vault.db");
    private static readonly string RulesPath       = Path.Combine(AppDataRoot, "rules.json");

    public static IServiceProvider Services { get; private set; } = null!;

    private MainShell? _shell;

    public App()
    {
        InitializeComponent();
        EnsureAppDataDirectories();
        Services = BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Ensure DB schema is up to date
        var repo = Services.GetRequiredService<ScreenshotRepository>();
        await repo.EnsureSchemaAsync();

        _shell = Services.GetRequiredService<MainShell>();
        _shell.Activate();
    }

    private static void EnsureAppDataDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(ScreenshotsRoot);

        // Pre-create default category folders
        foreach (var folder in new[] { "Miscellaneous", "Work", "Development", "Browser" })
            Directory.CreateDirectory(Path.Combine(ScreenshotsRoot, folder));
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // ── Core services ──────────────────────────────────────────────────
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<ContextExtractor>();
        services.AddSingleton<GlobalHookService>();

        services.AddSingleton<ActiveLearner>(_ => new ActiveLearner(RulesPath));

        services.AddSingleton<RulesEngine>(sp =>
        {
            var learner = sp.GetRequiredService<ActiveLearner>();
            // Rules provider lambda: always returns the live list from ActiveLearner
            return new RulesEngine(() => learner.Rules);
        });

        services.AddSingleton<ScreenshotRepository>(_ =>
            new ScreenshotRepository(
                () => new VaultDbContext(DbPath),
                ScreenshotsRoot));

        services.AddSingleton<CaptureOrchestrator>();

        // ── App services ───────────────────────────────────────────────────
        services.AddSingleton<ThemeService>();

        // ── ViewModels ─────────────────────────────────────────────────────
        services.AddTransient<GalleryViewModel>();
        services.AddTransient<SwipeModeViewModel>();
        services.AddTransient<MiscClassifyViewModel>();

        // ── Views ──────────────────────────────────────────────────────────
        services.AddTransient<GalleryView>();
        services.AddTransient<SwipeModeView>();
        services.AddTransient<MiscClassifyView>();
        services.AddSingleton<MainShell>();

        return services.BuildServiceProvider();
    }
}
