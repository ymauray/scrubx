using Xunit;
using Scrubx.Cli;

namespace Scrubx.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_WithPositionalPath_ReturnsInputPath()
    {
        // Arrange
        string[] args = ["document.docx"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.Equal("document.docx", options.InputPath);
        Assert.Null(options.ErrorMessage);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void Parse_WithHelpOption_ReturnsShowHelp()
    {
        // Arrange
        string[] args = ["--help"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.True(options.ShowHelp);
        Assert.Null(options.ErrorMessage);
        Assert.Null(options.InputPath);
    }

    [Fact]
    public void Parse_WithMissingInputPath_ReturnsErrorMessage()
    {
        // Arrange
        string[] args = [];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.NotNull(options.ErrorMessage);
        Assert.Contains("est requis", options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithTwoPositionalArguments_ReturnsErrorMessage()
    {
        // Arrange
        string[] args = ["document.docx", "autre.docx"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.NotNull(options.ErrorMessage);
        Assert.Contains("inattendu", options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithUnknownOption_ReturnsErrorMessage()
    {
        // Arrange
        string[] args = ["--invalid"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.NotNull(options.ErrorMessage);
        Assert.Contains("inconnu", options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithVerboseShortOption_ReturnsVerboseTrue()
    {
        // Arrange
        string[] args = ["document.docx", "-v"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.True(options.Verbose);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithVerboseLongOption_ReturnsVerboseTrue()
    {
        // Arrange
        string[] args = ["document.docx", "--verbose"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.True(options.Verbose);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithWarningShortOption_ReturnsShowWarningsTrue()
    {
        // Arrange
        string[] args = ["document.docx", "-w"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.True(options.ShowWarnings);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithWarningLongOption_ReturnsShowWarningsTrue()
    {
        // Arrange
        string[] args = ["document.docx", "--warning"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.True(options.ShowWarnings);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithoutWarningOption_ReturnsShowWarningsFalse()
    {
        // Arrange
        string[] args = ["document.docx"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.False(options.ShowWarnings);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithShowRulesShortOption_ReturnsShowRulesTrue()
    {
        // Arrange
        string[] args = ["-r"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.True(options.ShowRules);
        Assert.Null(options.ErrorMessage);
        Assert.Null(options.InputPath);
    }

    [Fact]
    public void Parse_WithShowRulesLongOption_ReturnsShowRulesTrue()
    {
        // Arrange
        string[] args = ["--show-rules"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.True(options.ShowRules);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithIgnoreOptionButNoValue_ReturnsErrorMessage()
    {
        // Arrange
        string[] args = ["document.docx", "-i"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.NotNull(options.ErrorMessage);
        Assert.Contains("manquant", options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithSingleIgnoreCode_ReturnsIgnoredRuleCode()
    {
        // Arrange
        string[] args = ["document.docx", "-i", "APOS"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.Equal(["APOS"], options.IgnoredRuleCodes);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithCommaSeparatedIgnoreCodes_ReturnsAllIgnoredRuleCodes()
    {
        // Arrange
        string[] args = ["document.docx", "-i", "APOS,GDROIT,TIRET"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.Equal(["APOS", "GDROIT", "TIRET"], options.IgnoredRuleCodes);
        Assert.Null(options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithRepeatedIgnoreOption_AccumulatesRuleCodes()
    {
        // Arrange
        string[] args = ["document.docx", "--ignore", "APOS", "-i", "GDROIT,TIRET"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.Equal(["APOS", "GDROIT", "TIRET"], options.IgnoredRuleCodes);
        Assert.Null(options.ErrorMessage);
    }
}
