using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// PrintJobHelper の印刷ジョブ名生成機能の単体テスト
/// </summary>
public class PrintJobHelperTests
{
    [Theory]
    [InlineData("sample.pdf", "sample - PDFBinder")]
    [InlineData("document.2024.pdf", "document.2024 - PDFBinder")]
    [InlineData(@"C:\Users\User\Documents\report.final.pdf", "report.final - PDFBinder")]
    [InlineData("test", "test - PDFBinder")]
    [InlineData("名称未設定.pdf", "名称未設定 - PDFBinder")]
    [InlineData("名称未設定.png", "名称未設定 - PDFBinder")]
    public void GenerateJobName_WithValidFileNames_ReturnsFormattedJobNameWithoutExtension(string input, string expected)
    {
        string actual = PrintJobHelper.GenerateJobName(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".pdf")]
    public void GenerateJobName_WithNullOrEmptyOrExtensionOnly_ReturnsDefaultTitleWithAppSuffix(string? input)
    {
        string actual = PrintJobHelper.GenerateJobName(input);
        Assert.Equal("名称未設定 - PDFBinder", actual);
    }
}
