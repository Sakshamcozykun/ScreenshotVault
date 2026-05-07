// src/ScreenshotVault.Core/Classification/ActiveLearner.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenshotVault.Core.Models;

namespace ScreenshotVault.Core.Classification;

/// <summary>
/// Online learning engine. When a user manually classifies a Miscellaneous
/// screenshot, this engine:
///   1. Penalises rules that wrongly fired for that screenshot.
///   2. Reinforces rules that correctly predicted the user's chosen category.
///   3. Synthesises a new rule if no existing rule covered the context.
///   4. Persists all changes to rules.json for immediate use by RulesEngine.
/// </summary>
public sealed class ActiveLearner
{
    // Hyperparameters — tuned for fast convergence on small datasets
    private const double LearningRate        = 0.15;  // Initial weight of synthesised rules
    private const double ReinforcementBonus  = 0.10;  // Added when rule is confirmed correct
    private const double PenaltyFactor       = 0.20;  // Subtracted when rule fires wrongly
    private const double MaxWeight           = 1.00;
    private const double MinWeight           = 0.00;

    private readonly string _rulesPath;
    private readonly object _lock = new();
    private List<ClassificationRule> _rules;

    public ActiveLearner(string rulesPath)
    {
        _rulesPath = rulesPath;
        _rules     = LoadRules();
    }

    /// <summary>Exposes live rule list to RulesEngine via lambda.</summary>
    public List<ClassificationRule> Rules
    {
        get { lock (_lock) { return _rules; } }
    }

    /// <summary>
    /// Call this whenever the user assigns a category to a screenshot
    /// (drag-to-folder, MiscClassifyView dropdown, etc.).
    /// Thread-safe.
    /// </summary>
    public void RecordCorrection(WindowContext context, string assignedCategory)
    {
        var features = RulesEngine.ExtractFeatures(context);

        lock (_lock)
        {
            bool existingRuleReinforced = false;

            foreach (var rule in _rules.Where(r => r.IsActive))
            {
                bool matches        = RulesEngine.MatchesAll(rule, features);
                bool correctCategory = rule.Category.Equals(assignedCategory,
                    StringComparison.OrdinalIgnoreCase);

                if (matches && !correctCategory)
                {
                    // Rule fired but predicted wrong category → penalise
                    rule.MissCount++;
                    rule.Weight = Math.Max(MinWeight, rule.Weight - PenaltyFactor);
                }
                else if (matches && correctCategory)
                {
                    // Rule fired and was correct → reinforce
                    rule.HitCount++;
                    rule.Weight = Math.Min(MaxWeight, rule.Weight + ReinforcementBonus);
                    existingRuleReinforced = true;
                }
            }

            if (!existingRuleReinforced)
                TrySynthesiseNewRule(context, features, assignedCategory);

            // Remove rules that have decayed below the pruning threshold
            // (preserve user-defined rules always)
            _rules = _rules.Where(r => r.IsActive).ToList();

            PersistRules();
        }
    }

    /// <summary>
    /// Creates a new rule from the strongest signal in the context.
    /// Priority: URL domain > ProcessName > WindowTitle keyword.
    /// </summary>
    private void TrySynthesiseNewRule(
        WindowContext context,
        Dictionary<string, string> features,
        string category)
    {
        var predicates = new List<RulePredicate>();

        // 1. URL domain is the most precise signal
        if (!string.IsNullOrWhiteSpace(context.Url))
        {
            var domain = ExtractDomain(context.Url);
            if (domain != null)
            {
                predicates.Add(new RulePredicate("Url", domain, MatchType.Contains));
                goto AddRule;
            }
        }

        // 2. Fall back to process name (e.g. "Teams", "WINWORD")
        if (!string.IsNullOrWhiteSpace(context.ProcessName))
        {
            predicates.Add(new RulePredicate(
                "ProcessName", context.ProcessName!, MatchType.Exact));
            goto AddRule;
        }

        // 3. Last resort: first significant word from window title
        if (!string.IsNullOrWhiteSpace(context.WindowTitle))
        {
            var keyword = ExtractTitleKeyword(context.WindowTitle!);
            if (keyword != null)
                predicates.Add(new RulePredicate("WindowTitle", keyword, MatchType.Contains));
        }

        AddRule:
        if (predicates.Count == 0) return; // Not enough signal

        _rules.Add(new ClassificationRule
        {
            Category      = category,
            Predicates    = predicates,
            Weight        = LearningRate,   // Conservative start — needs reinforcement
            IsUserDefined = false,
        });
    }

    private static string? ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // "www.github.com" → "github.com" for broader matching
            var host = uri.Host.ToLowerInvariant();
            return host.StartsWith("www.") ? host[4..] : host;
        }
        return null;
    }

    private static string? ExtractTitleKeyword(string title)
    {
        // Skip generic words, grab the first meaningful token (≥4 chars)
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "the", "and", "for", "new", "tab", "page", "with", "from" };

        return title
            .Split([' ', '-', '|', '–', '—'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(w => w.Length >= 4 && !stopWords.Contains(w));
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private void PersistRules()
    {
        try
        {
            var dir = Path.GetDirectoryName(_rulesPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_rules, JsonOptions);
            // Atomic write: write to temp file, then rename
            var tmp = _rulesPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _rulesPath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActiveLearner] Persist failed: {ex.Message}");
        }
    }

    private List<ClassificationRule> LoadRules()
    {
        try
        {
            if (File.Exists(_rulesPath))
            {
                var json  = File.ReadAllText(_rulesPath);
                var rules = JsonSerializer.Deserialize<List<ClassificationRule>>(json, JsonOptions);
                if (rules?.Count > 0) return rules;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActiveLearner] Load failed: {ex.Message}");
        }
        return GetDefaultRules();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented         = true,
        Converters            = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    // ── Seed Rules ───────────────────────────────────────────────────────────

    private static List<ClassificationRule> GetDefaultRules() =>
    [
        // ── Work ──────────────────────────────────────────────────────────
        new() { Category = "Work", Weight = 0.85, IsUserDefined = true,
            Predicates = [new("ProcessName", "Teams",   MatchType.Contains)] },
        new() { Category = "Work", Weight = 0.85, IsUserDefined = true,
            Predicates = [new("ProcessName", "WINWORD", MatchType.Exact)] },
        new() { Category = "Work", Weight = 0.85, IsUserDefined = true,
            Predicates = [new("ProcessName", "EXCEL",   MatchType.Exact)] },
        new() { Category = "Work", Weight = 0.80, IsUserDefined = true,
            Predicates = [new("ProcessName", "POWERPNT",MatchType.Exact)] },
        new() { Category = "Work", Weight = 0.75, IsUserDefined = true,
            Predicates = [new("Url", "outlook.office.com", MatchType.Contains)] },

        // ── Development ───────────────────────────────────────────────────
        new() { Category = "Development", Weight = 0.90, IsUserDefined = true,
            Predicates = [new("ProcessName", "Code",    MatchType.Exact)] },        // VS Code
        new() { Category = "Development", Weight = 0.90, IsUserDefined = true,
            Predicates = [new("ProcessName", "devenv",  MatchType.Exact)] },        // Visual Studio
        new() { Category = "Development", Weight = 0.85, IsUserDefined = true,
            Predicates = [new("Url", "github.com",      MatchType.Contains)] },
        new() { Category = "Development", Weight = 0.80, IsUserDefined = true,
            Predicates = [new("Url", "stackoverflow.com", MatchType.Contains)] },

        // ── Browser (generic fallback for any browser without URL match) ──
        new() { Category = "Browser", Weight = 0.40,
            Predicates = [new("ProcessName", "chrome",  MatchType.Exact)] },
        new() { Category = "Browser", Weight = 0.40,
            Predicates = [new("ProcessName", "msedge",  MatchType.Exact)] },
        new() { Category = "Browser", Weight = 0.40,
            Predicates = [new("ProcessName", "firefox", MatchType.Exact)] },
    ];
}
