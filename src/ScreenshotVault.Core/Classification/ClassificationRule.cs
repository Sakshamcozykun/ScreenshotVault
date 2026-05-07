// src/ScreenshotVault.Core/Classification/ClassificationRule.cs
namespace ScreenshotVault.Core.Classification;

public enum MatchType { Contains, Regex, Exact }

public record RulePredicate(string Field, string Pattern, MatchType Type);

public sealed class ClassificationRule
{
    public Guid   Id         { get; init; } = Guid.NewGuid();
    public string Category   { get; set; }  = "";

    /// <summary>All predicates must match (AND semantics).</summary>
    public List<RulePredicate> Predicates { get; set; } = [];

    /// <summary>Confidence score in [0..1]. Rules below 0.15 are pruned.</summary>
    public double Weight    { get; set; } = 1.0;
    public int    HitCount  { get; set; }
    public int    MissCount { get; set; }

    /// <summary>User-created rules are protected from auto-pruning.</summary>
    public bool IsUserDefined { get; set; }

    public bool IsActive => IsUserDefined || Weight > 0.15;
}
