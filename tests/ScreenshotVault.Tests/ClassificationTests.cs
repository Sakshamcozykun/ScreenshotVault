// tests/ScreenshotVault.Tests/ClassificationTests.cs
using ScreenshotVault.Core.Classification;
using ScreenshotVault.Core.Models;

namespace ScreenshotVault.Tests;

public sealed class RulesEngineTests
{
    private static RulesEngine BuildEngine(List<ClassificationRule> rules)
        => new RulesEngine(() => rules);

    [Fact]
    public void Classify_BrowserUrl_ReturnsCorrectCategory()
    {
        var rules = new List<ClassificationRule>
        {
            new() { Category = "Development", Weight = 0.9,
                Predicates = [new("Url", "github.com", MatchType.Contains)] }
        };
        var engine  = BuildEngine(rules);
        var context = new WindowContext { Url = "https://github.com/user/repo" };

        Assert.Equal("Development", engine.Classify(context));
    }

    [Fact]
    public void Classify_NoMatch_ReturnsMiscellaneous()
    {
        var engine  = BuildEngine([]);
        var context = new WindowContext { ProcessName = "notepad" };

        Assert.Equal("Miscellaneous", engine.Classify(context));
    }

    [Fact]
    public void Classify_LowWeight_ReturnsMiscellaneous()
    {
        var rules = new List<ClassificationRule>
        {
            new() { Category = "Work", Weight = 0.10, // Below 0.30 threshold
                Predicates = [new("ProcessName", "notepad", MatchType.Exact)] }
        };
        var engine  = BuildEngine(rules);
        var context = new WindowContext { ProcessName = "notepad" };

        Assert.Equal("Miscellaneous", engine.Classify(context));
    }

    [Fact]
    public void Classify_MultiplePredicates_AllMustMatch()
    {
        var rules = new List<ClassificationRule>
        {
            new() { Category = "Work", Weight = 0.9,
                Predicates = [
                    new("ProcessName", "chrome",   MatchType.Exact),
                    new("Url",         "slack.com", MatchType.Contains)
                ] }
        };
        var engine = BuildEngine(rules);

        // Only process matches, not URL → should NOT match
        Assert.Equal("Miscellaneous",
            engine.Classify(new WindowContext { ProcessName = "chrome", Url = "google.com" }));

        // Both match → should classify
        Assert.Equal("Work",
            engine.Classify(new WindowContext { ProcessName = "chrome", Url = "https://app.slack.com" }));
    }
}

public sealed class ActiveLearnerTests
{
    private static string TempRulesPath()
        => Path.Combine(Path.GetTempPath(), $"rules_{Guid.NewGuid()}.json");

    [Fact]
    public void RecordCorrection_SynthesisesNewRule_WhenNoMatchExists()
    {
        var path    = TempRulesPath();
        var learner = new ActiveLearner(path);
        var ctx     = new WindowContext { ProcessName = "UnknownApp" };

        learner.RecordCorrection(ctx, "CustomCategory");

        var newRule = learner.Rules.FirstOrDefault(r =>
            r.Category == "CustomCategory" &&
            r.Predicates.Any(p => p.Pattern == "UnknownApp"));

        Assert.NotNull(newRule);
        Assert.True(newRule.Weight > 0);
    }

    [Fact]
    public void RecordCorrection_ReinforcesExistingCorrectRule()
    {
        var path    = TempRulesPath();
        var learner = new ActiveLearner(path);

        // Manually add a rule that correctly predicts "Work"
        var initialWeight = 0.5;
        learner.Rules.Add(new ClassificationRule
        {
            Category   = "Work",
            Weight     = initialWeight,
            Predicates = [new("ProcessName", "Teams", MatchType.Contains)]
        });

        learner.RecordCorrection(
            new WindowContext { ProcessName = "Teams" }, "Work");

        var rule = learner.Rules.First(r => r.Category == "Work" &&
            r.Predicates.Any(p => p.Pattern == "Teams"));

        Assert.True(rule.Weight > initialWeight);
    }

    [Fact]
    public void RecordCorrection_PenalisesWrongRule()
    {
        var path    = TempRulesPath();
        var learner = new ActiveLearner(path);

        var initialWeight = 0.8;
        learner.Rules.Add(new ClassificationRule
        {
            Category   = "Browser",      // Wrong — user says it's "Work"
            Weight     = initialWeight,
            Predicates = [new("ProcessName", "chrome", MatchType.Exact)]
        });

        learner.RecordCorrection(
            new WindowContext { ProcessName = "chrome" }, "Work");

        var rule = learner.Rules.FirstOrDefault(r =>
            r.Category == "Browser" &&
            r.Predicates.Any(p => p.Pattern == "chrome"));

        // Rule may be pruned (weight → 0) or have reduced weight
        if (rule != null)
            Assert.True(rule.Weight < initialWeight);
    }

    [Fact]
    public void RecordCorrection_PrefersUrlDomain_OverProcessName()
    {
        var path    = TempRulesPath();
        var learner = new ActiveLearner(path);

        learner.RecordCorrection(
            new WindowContext
            {
                ProcessName = "chrome",
                Url         = "https://www.figma.com/file/abc"
            },
            "Design");

        // Should synthesise a URL-based rule (domain: figma.com)
        var urlRule = learner.Rules.FirstOrDefault(r =>
            r.Category == "Design" &&
            r.Predicates.Any(p => p.Field == "Url" && p.Pattern == "figma.com"));

        Assert.NotNull(urlRule);
    }

    [Fact]
    public void Rules_ArePersistedAndReloaded()
    {
        var path    = TempRulesPath();
        var learner = new ActiveLearner(path);

        learner.RecordCorrection(
            new WindowContext { ProcessName = "PersistTest" }, "TestCategory");

        // Create a fresh instance loading from the same path
        var reloaded = new ActiveLearner(path);
        Assert.Contains(reloaded.Rules, r => r.Category == "TestCategory");
    }
}

// ── Test project file ──────────────────────────────────────────────────────
// tests/ScreenshotVault.Tests/ScreenshotVault.Tests.csproj
/*
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ScreenshotVault.Core\ScreenshotVault.Core.csproj"/>
  </ItemGroup>
</Project>
*/
