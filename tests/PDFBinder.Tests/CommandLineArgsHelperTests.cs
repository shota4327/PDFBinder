using System.IO;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="CommandLineArgsHelper"/> の単体テスト
/// </summary>
public class CommandLineArgsHelperTests
{
    [Fact]
    public void Parse_WithNullArgs_ReturnsNullPrimaryAndEmptyAdditional()
    {
        var result = CommandLineArgsHelper.Parse(null);

        Assert.Null(result.PrimaryFile);
        Assert.Empty(result.AdditionalFiles);
    }

    [Fact]
    public void Parse_WithEmptyArgs_ReturnsNullPrimaryAndEmptyAdditional()
    {
        var result = CommandLineArgsHelper.Parse(Array.Empty<string>());

        Assert.Null(result.PrimaryFile);
        Assert.Empty(result.AdditionalFiles);
    }

    [Fact]
    public void Parse_WithSinglePdfFile_ReturnsPrimaryFileAndEmptyAdditional()
    {
        var samplePath = Path.Combine(Path.GetTempPath(), "sample.pdf");
        var result = CommandLineArgsHelper.Parse(new[] { samplePath });

        Assert.Equal(Path.GetFullPath(samplePath), result.PrimaryFile);
        Assert.Empty(result.AdditionalFiles);
    }

    [Fact]
    public void Parse_WithMultiplePdfFiles_SeparatesFirstAndAdditionalFiles()
    {
        var file1 = Path.Combine(Path.GetTempPath(), "doc1.pdf");
        var file2 = Path.Combine(Path.GetTempPath(), "doc2.pdf");
        var file3 = Path.Combine(Path.GetTempPath(), "doc3.pdf");

        var result = CommandLineArgsHelper.Parse(new[] { file1, file2, file3 });

        Assert.Equal(Path.GetFullPath(file1), result.PrimaryFile);
        Assert.Equal(2, result.AdditionalFiles.Count);
        Assert.Equal(Path.GetFullPath(file2), result.AdditionalFiles[0]);
        Assert.Equal(Path.GetFullPath(file3), result.AdditionalFiles[1]);
    }

    [Fact]
    public void Parse_WithQuotedPathAndWhitespace_NormalizesProperly()
    {
        var rawPath = $" \"{Path.Combine(Path.GetTempPath(), "sample with spaces.pdf")}\" ";
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sample with spaces.pdf"));

        var result = CommandLineArgsHelper.Parse(new[] { rawPath });

        Assert.Equal(expected, result.PrimaryFile);
        Assert.Empty(result.AdditionalFiles);
    }

    [Fact]
    public void Parse_WithNonPdfAndOptions_FiltersOutInvalidEntries()
    {
        var validPdf = Path.Combine(Path.GetTempPath(), "valid.pdf");
        var args = new[]
        {
            "--option",
            "/v",
            Path.Combine(Path.GetTempPath(), "text.txt"),
            Path.Combine(Path.GetTempPath(), "doc.docx"),
            validPdf,
            ""
        };

        var result = CommandLineArgsHelper.Parse(args);

        Assert.Equal(Path.GetFullPath(validPdf), result.PrimaryFile);
        Assert.Empty(result.AdditionalFiles);
    }

    [Fact]
    public void Parse_WithImageFiles_AcceptsSupportedImages()
    {
        var validPng = Path.Combine(Path.GetTempPath(), "image.png");
        var validJpg = Path.Combine(Path.GetTempPath(), "photo.jpg");
        var args = new[] { validPng, validJpg };

        var result = CommandLineArgsHelper.Parse(args);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(Path.GetFullPath(validPng), result.Files[0]);
        Assert.Equal(Path.GetFullPath(validJpg), result.Files[1]);
    }

    [Fact]
    public void CreateProcessStartInfo_GeneratesQuotedArgument()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), "target.pdf");
        var startInfo = CommandLineArgsHelper.CreateProcessStartInfo(targetPath);

        Assert.NotNull(startInfo);
        Assert.Contains(targetPath, startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
    }

    [Theory]
    [InlineData("--new-window")]
    [InlineData("--NEW-WINDOW")]
    [InlineData("-n")]
    [InlineData("-N")]
    public void Parse_WithNewWindowFlag_SetsForceNewWindowTrue(string flag)
    {
        var samplePath = Path.Combine(Path.GetTempPath(), "test.pdf");
        var result = CommandLineArgsHelper.Parse(new[] { flag, samplePath });

        Assert.True(result.ForceNewWindow);
        Assert.Single(result.Files);
        Assert.Equal(Path.GetFullPath(samplePath), result.Files[0]);
    }

    [Fact]
    public void Parse_WithoutNewWindowFlag_SetsForceNewWindowFalse()
    {
        var samplePath = Path.Combine(Path.GetTempPath(), "test.pdf");
        var result = CommandLineArgsHelper.Parse(new[] { samplePath });

        Assert.False(result.ForceNewWindow);
        Assert.Single(result.Files);
        Assert.Equal(Path.GetFullPath(samplePath), result.Files[0]);
    }

    [Fact]
    public void Parse_WithDuplicateFiles_DeduplicatesEntries()
    {
        var samplePath = Path.Combine(Path.GetTempPath(), "duplicate.pdf");
        var result = CommandLineArgsHelper.Parse(new[] { samplePath, samplePath });

        Assert.Single(result.Files);
        Assert.Equal(Path.GetFullPath(samplePath), result.Files[0]);
    }
}
