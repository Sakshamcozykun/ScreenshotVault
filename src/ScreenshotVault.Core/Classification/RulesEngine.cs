// src/ScreenshotVault.Core/Classification/RulesEngine.cs
using System.Text.RegularExpressions;
using ScreenshotVault.Core.Models;

namespace ScreenshotVault.Core.Classification;

/// <summary>
/// Classifies a WindowContext into a category folder name.
/// Loads rules from the ActiveLearner's shared rule list (hot-reloadable).
/// </summary>
public sealed class RulesEngine
{
    private const double MinConfidenceThreshold = 0.30;

    private readonly Func<List<ClassificationRule>> _rulesProvider;

    /// <param name="rulesProvider">
    /// Lambda returning the current rule list — allows hot-reload
    /// without restarting the engine after ActiveLearner persists changes.
    /// </param>
    public RulesEngine(Func<List<ClassificationRule>> rulesProvider)
    {
        _rulesProvider = rulesProvider;
    }

    /// <summary>
    /// Returns the best matching category name, or "Miscellaneous" if no
    /// rule meets the confidence threshold.
    /// </summary>
    public string Classify(WindowContext context)
    {
        var features   = ExtractFeatures(context);
        var rules      = _rulesProvider();

        var candidates = rules
            .Where(r => r.IsActive && MatchesAll(r, features))
            .OrderByDescending(r => r.Weight)
            .ThenByDescending(r => r.HitCount)
            .ToList();

        if (candidates.Count > 0 && candidates[0].Weight >= MinConfidenceThreshold)
            return candidates[0].Category;

        return "Miscellaneous";
    }

    internal static bool MatchesAll(ClassificationRule rule,
        Dictionary<string, string> features)
    {
        return rule.Predicates.All(pred =>
        {
            if (!features.TryGetValue(pred.Field, out var value))
                return false;

            return pred.Type switch
            {
                MatchType.Contains => value.Contains(pred.Pattern,
                                         StringComparison.OrdinalIgnoreCase),
                MatchType.Exact    => value.Equals(pred.Pattern,
                                         StringComparison.OrdinalIgnoreCase),
                MatchType.Regex    => SafeRegexMatch(value, pred.Pattern),
                _                  => false
            };
        });
    }

    private static bool SafeRegexMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException) { return false; }
        catch (ArgumentException)          { return false; }
    }

    internal static Dictionary<string, string> ExtractFeatures(WindowContext ctx)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["ProcessName"] = ctx.ProcessName ?? "",
            ["WindowTitle"] = ctx.WindowTitle ?? "",
            ["Url"]         = ctx.Url         ?? "",
        };
}
