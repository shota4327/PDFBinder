using System.IO;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Exceptions;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PDFBinder.Tests.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> のパスワード保護PDF対応に関する単体テスト
/// </summary>
public class MainViewModelPasswordTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PdfService _pdfService;

    public MainViewModelPasswordTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_VmPasswordTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _pdfService = new PdfService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private string CreateProtectedPdf(string fileName, string password, int pageCount = 2)
    {
        string filePath = Path.Combine(_testDirectory, fileName);
        using var doc = new PdfDocument();
        doc.SecuritySettings.UserPassword = password;
        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(400);
            page.Height = XUnit.FromPoint(600);
        }
        doc.Save(filePath);
        return filePath;
    }

    [Fact]
    public async Task LoadDocumentWithPasswordPromptAsync_WithCorrectPassword_LoadsDocument()
    {
        // Arrange
        string filePath = CreateProtectedPdf("protected1.pdf", "secret", 2);
        var vm = new MainViewModel(_pdfService);

        int promptCount = 0;
        vm.PromptPasswordCallback = (fileName, error) =>
        {
            promptCount++;
            Assert.Equal("protected1.pdf", fileName);
            Assert.Null(error);
            return Task.FromResult<string?>("secret");
        };

        // Act
        var doc = await vm.LoadDocumentWithPasswordPromptAsync(filePath);

        // Assert
        Assert.NotNull(doc);
        Assert.Equal(2, doc.PageCount);
        Assert.Equal("secret", doc.DocumentPassword);
        Assert.Equal(1, promptCount);
    }

    [Fact]
    public async Task LoadDocumentWithPasswordPromptAsync_WithWrongPasswordThenCorrect_RetriesWithError()
    {
        // Arrange
        string filePath = CreateProtectedPdf("protected2.pdf", "correctpass", 1);
        var vm = new MainViewModel(_pdfService);

        var errorMessages = new List<string?>();
        int attempt = 0;
        vm.PromptPasswordCallback = (fileName, error) =>
        {
            errorMessages.Add(error);
            attempt++;
            if (attempt == 1)
            {
                return Task.FromResult<string?>("wrongpass");
            }
            return Task.FromResult<string?>("correctpass");
        };

        // Act
        var doc = await vm.LoadDocumentWithPasswordPromptAsync(filePath);

        // Assert
        Assert.NotNull(doc);
        Assert.Equal(1, doc.PageCount);
        Assert.Equal(2, attempt);
        Assert.Null(errorMessages[0]);
        Assert.NotNull(errorMessages[1]);
        Assert.Contains("パスワードが正しくありません", errorMessages[1]);
    }

    [Fact]
    public async Task LoadDocumentWithPasswordPromptAsync_WhenCancelled_ReturnsNull()
    {
        // Arrange
        string filePath = CreateProtectedPdf("protected3.pdf", "secret", 1);
        var vm = new MainViewModel(_pdfService);

        vm.PromptPasswordCallback = (fileName, error) => Task.FromResult<string?>(null);

        // Act
        var doc = await vm.LoadDocumentWithPasswordPromptAsync(filePath);

        // Assert
        Assert.Null(doc);
    }

    [Fact]
    public async Task PasswordPrompt_ConfirmCommand_ResolvesPromptTask()
    {
        // Arrange
        var vm = new MainViewModel(_pdfService);

        // Act
        var promptTask = vm.PromptPasswordAsync("sample.pdf");
        Assert.True(vm.IsPasswordPromptVisible);
        Assert.Equal("sample.pdf", vm.PasswordPromptFileName);

        vm.PasswordPromptInput = "userpassword";
        vm.ConfirmPasswordPromptCommand.Execute(null);

        var result = await promptTask;

        // Assert
        Assert.False(vm.IsPasswordPromptVisible);
        Assert.Equal("userpassword", result);
    }

    [Fact]
    public async Task PasswordPrompt_CancelCommand_ResolvesNull()
    {
        // Arrange
        var vm = new MainViewModel(_pdfService);

        // Act
        var promptTask = vm.PromptPasswordAsync("sample.pdf");
        Assert.True(vm.IsPasswordPromptVisible);

        vm.CancelPasswordPromptCommand.Execute(null);

        var result = await promptTask;

        // Assert
        Assert.False(vm.IsPasswordPromptVisible);
        Assert.Null(result);
    }

    [Fact]
    public void PasswordPrompt_ToggleMaskCommand_TogglesMaskState()
    {
        // Arrange
        var vm = new MainViewModel(_pdfService);
        Assert.True(vm.IsPasswordInputMasked);

        // Act & Assert
        vm.TogglePasswordMaskCommand.Execute(null);
        Assert.False(vm.IsPasswordInputMasked);

        vm.TogglePasswordMaskCommand.Execute(null);
        Assert.True(vm.IsPasswordInputMasked);
    }
}
