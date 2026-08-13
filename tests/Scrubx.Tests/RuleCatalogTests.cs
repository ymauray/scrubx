using Xunit;
using Scrubx.Cli;

namespace Scrubx.Tests;

public class RuleCatalogTests
{
    [Fact]
    public void All_RuleCodes_AreUnique()
    {
        var codes = RuleCatalog.All.Select(r => r.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void GetByCode_WithKnownCode_ReturnsMatchingRule()
    {
        var rule = RuleCatalog.GetByCode("APOS");

        Assert.NotNull(rule);
        Assert.Equal("ApostropheDroite", rule!.RuleName);
    }

    [Fact]
    public void GetByCode_IsCaseInsensitive()
    {
        var rule = RuleCatalog.GetByCode("apos");

        Assert.NotNull(rule);
        Assert.Equal("ApostropheDroite", rule!.RuleName);
    }

    [Fact]
    public void GetByCode_WithUnknownCode_ReturnsNull()
    {
        var rule = RuleCatalog.GetByCode("INCONNU");

        Assert.Null(rule);
    }
}
