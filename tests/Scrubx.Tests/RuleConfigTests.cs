using Xunit;
using Scrubx.Cli;

namespace Scrubx.Tests;

public class RuleConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"scrubx-test-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void CreateOrUpdate_OnNewFile_CreatesAllRulesEnabled()
    {
        var added = RuleConfig.CreateOrUpdate(_path);

        Assert.Equal(RuleCatalog.All.Count, added);
        Assert.True(File.Exists(_path));

        var content = File.ReadAllText(_path);
        foreach (var rule in RuleCatalog.All)
        {
            Assert.Contains($"\"{rule.Code}\"", content);
        }
    }

    [Fact]
    public void CreateOrUpdate_OnExistingFile_OnlyAddsMissingRulesAndKeepsExistingValues()
    {
        File.WriteAllText(_path, $$"""{ "{{RuleCatalog.All[0].Code}}": false }""");

        var added = RuleConfig.CreateOrUpdate(_path);

        Assert.Equal(RuleCatalog.All.Count - 1, added);

        var enabledRules = RuleConfig.TryLoadEnabledRuleNames(_path, out var error);
        Assert.Null(error);
        Assert.NotNull(enabledRules);
        Assert.DoesNotContain(RuleCatalog.All[0].RuleName, enabledRules);
        Assert.Contains(RuleCatalog.All[1].RuleName, enabledRules);
    }

    [Fact]
    public void CreateOrUpdate_CalledTwice_IsIdempotent()
    {
        RuleConfig.CreateOrUpdate(_path);
        var addedSecondTime = RuleConfig.CreateOrUpdate(_path);

        Assert.Equal(0, addedSecondTime);
    }

    [Fact]
    public void CreateOrUpdate_OnMalformedFile_ReturnsNull()
    {
        File.WriteAllText(_path, "ceci n'est pas du json");

        var added = RuleConfig.CreateOrUpdate(_path);

        Assert.Null(added);
    }

    [Fact]
    public void TryLoadEnabledRuleNames_WithNoFile_ReturnsAllRuleNames()
    {
        var enabledRules = RuleConfig.TryLoadEnabledRuleNames(_path, out var error);

        Assert.Null(error);
        Assert.NotNull(enabledRules);
        Assert.True(RuleCatalog.AllRuleNames.SetEquals(enabledRules));
    }

    [Fact]
    public void TryLoadEnabledRuleNames_WithDisabledCode_ExcludesThatRule()
    {
        File.WriteAllText(_path, $$"""{ "{{RuleCatalog.All[0].Code}}": false }""");

        var enabledRules = RuleConfig.TryLoadEnabledRuleNames(_path, out var error);

        Assert.Null(error);
        Assert.NotNull(enabledRules);
        Assert.DoesNotContain(RuleCatalog.All[0].RuleName, enabledRules);
        Assert.Equal(RuleCatalog.All.Count - 1, enabledRules.Count);
    }

    [Fact]
    public void TryLoadEnabledRuleNames_WithUnknownCodeInFile_IsIgnoredWithoutError()
    {
        File.WriteAllText(_path, """{ "CODE_INEXISTANT": false }""");

        var enabledRules = RuleConfig.TryLoadEnabledRuleNames(_path, out var error);

        Assert.Null(error);
        Assert.NotNull(enabledRules);
        Assert.True(RuleCatalog.AllRuleNames.SetEquals(enabledRules));
    }

    [Fact]
    public void TryLoadEnabledRuleNames_WithMalformedJson_ReturnsNullAndErrorMessage()
    {
        File.WriteAllText(_path, "ceci n'est pas du json");

        var enabledRules = RuleConfig.TryLoadEnabledRuleNames(_path, out var error);

        Assert.Null(enabledRules);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryLoadEnabledRuleNames_WithNonBooleanValue_ReturnsNullAndErrorMessage()
    {
        File.WriteAllText(_path, $$"""{ "{{RuleCatalog.All[0].Code}}": "oui" }""");

        var enabledRules = RuleConfig.TryLoadEnabledRuleNames(_path, out var error);

        Assert.Null(enabledRules);
        Assert.NotNull(error);
    }
}
