using Xunit;
using Scrubx.Cli;

namespace Scrubx.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_WithValidInputPathShortOption_ReturnsInputPath()
    {
        // Arrange
        string[] args = ["-i", "document.docx"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.Equal("document.docx", options.InputPath);
        Assert.Null(options.ErrorMessage);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void Parse_WithValidInputPathLongOption_ReturnsInputPath()
    {
        // Arrange
        string[] args = ["--input", "document.docx"];

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
        Assert.Contains("est requise", options.ErrorMessage);
    }

    [Fact]
    public void Parse_WithInputOptionButNoValue_ReturnsErrorMessage()
    {
        // Arrange
        string[] args = ["-i"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.NotNull(options.ErrorMessage);
        Assert.Contains("manquant", options.ErrorMessage);
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
        string[] args = ["-i", "document.docx", "-v"];

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
        string[] args = ["-i", "document.docx", "--verbose"];

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
        string[] args = ["-i", "document.docx", "-w"];

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
        string[] args = ["-i", "document.docx", "--warning"];

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
        string[] args = ["-i", "document.docx"];

        // Act
        var options = ArgumentParser.Parse(args);

        // Assert
        Assert.False(options.ShowWarnings);
        Assert.Null(options.ErrorMessage);
    }
}
