using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// ステータスバー表示内容の厳選およびエラーダイアログ昇格に関する単体テスト (Issue #63)
/// </summary>
public class StatusBarAndErrorDialogTests
{
    [Fact]
    public void DetailEditor_WhenPageChanged_DoesNotOverwriteStatusMessage()
    {
        // Arrange
        var vm = new MainViewModel();
        var page1 = new PdfPageModel { PageNumber = 1 };
        var page2 = new PdfPageModel { PageNumber = 2 };
        vm.Document.AddPage(page1);
        vm.Document.AddPage(page2);

        vm.StatusMessage = "保持されるべきステータスメッセージ";
        vm.IsDetailViewActive = true;

        // Act: 詳細エディタのページをスクロール・切り替え
        vm.DetailEditor?.InitializeDocument(vm.Document);
        vm.DetailEditor?.ScrollToPage(page2);

        // Assert: 以前のように「ページ 2 / 2」で上書きされず、メッセージが保持されること
        Assert.Equal("保持されるべきステータスメッセージ", vm.StatusMessage);
    }

    [Fact]
    public void OpenPageDetail_DoesNotOverwriteStatusMessage()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel { PageNumber = 1 };
        vm.Document.AddPage(page);
        vm.StatusMessage = "既存の通知メッセージ";

        // Act: グリッドから詳細ビューへ遷移
        vm.OpenPageDetail(page);

        // Assert: 「ページ 1 を編集しています。」で上書きされず、メッセージが保持されること
        Assert.Equal("既存の通知メッセージ", vm.StatusMessage);
    }

    [Fact]
    public async Task SaveDocumentAsync_WhenServiceThrows_ShowsErrorDialogAndSetsBriefStatus()
    {
        // Arrange
        var testService = new MockErrorPdfService
        {
            ThrowOnSave = new IOException("ディスク容量が不足しています。")
        };
        var vm = new MainViewModel(pdfService: testService);
        vm.Document.AddPage(new PdfPageModel());
        vm.Document.FilePath = @"C:\Test\Sample.pdf";
        vm.Document.IsModified = true;

        string? promptedTitle = null;
        string? promptedMessage = null;
        vm.ShowErrorPrompt = (title, message) =>
        {
            promptedTitle = title;
            promptedMessage = message;
        };

        // Act: 保存を実行
        bool result = await vm.SaveDocumentAsync();

        // Assert: 失敗（false）となり、エラーダイアログが表示され、簡潔な文言がステータスバーに設定されること
        Assert.False(result);
        Assert.True(vm.IsErrorDialogVisible);
        Assert.Equal("保存エラー", vm.ErrorDialogTitle);
        Assert.Contains("ディスク容量が不足しています。", vm.ErrorDialogMessage);
        Assert.Equal("保存エラー", promptedTitle);
        Assert.Contains("ディスク容量が不足しています。", promptedMessage);
        Assert.Equal("保存に失敗しました。", vm.StatusMessage);

        // Act: ダイアログを閉じる
        vm.CloseErrorDialogCommand.Execute(null);

        // Assert: ダイアログが非表示になること
        Assert.False(vm.IsErrorDialogVisible);
    }

    [Fact]
    public async Task ExportSelectedPagesAsync_WhenNoPageSelected_ShowsWarningDialog()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.Document.AddPage(new PdfPageModel { IsSelected = false });

        string? promptedTitle = null;
        string? promptedMessage = null;
        vm.ShowErrorPrompt = (title, message) =>
        {
            promptedTitle = title;
            promptedMessage = message;
        };

        // Act: ページ未選択状態でエクスポートを実行
        await vm.ExportSelectedPagesAsync();

        // Assert: ページ未選択の警告ダイアログが表示されること
        Assert.True(vm.IsErrorDialogVisible);
        Assert.Equal("ページ未選択", vm.ErrorDialogTitle);
        Assert.Equal("エクスポートするページを選択してください。", vm.ErrorDialogMessage);
        Assert.Equal("ページ未選択", promptedTitle);
        Assert.Equal("エクスポートが中止されました。", vm.StatusMessage);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenServiceThrows_ShowsErrorDialogAndSetsBriefStatus()
    {
        // Arrange
        var testService = new MockErrorPdfService
        {
            ThrowOnLoad = new InvalidDataException("PDFファイルが破損しています。")
        };
        var vm = new MainViewModel(pdfService: testService);

        string? promptedTitle = null;
        string? promptedMessage = null;
        vm.ShowErrorPrompt = (title, message) =>
        {
            promptedTitle = title;
            promptedMessage = message;
        };

        // Act: 破損PDFの読み込みを実行
        await vm.OpenDocumentAsync(@"C:\Test\Corrupt.pdf");

        // Assert: エラーダイアログが表示され、簡潔なステータスが設定されること
        Assert.True(vm.IsErrorDialogVisible);
        Assert.Equal("ファイル読み込みエラー", vm.ErrorDialogTitle);
        Assert.Contains("PDFファイルが破損しています。", vm.ErrorDialogMessage);
        Assert.Equal("ファイル読み込みエラー", promptedTitle);
        Assert.Equal("読み込みに失敗しました。", vm.StatusMessage);
    }

    [Fact]
    public async Task AppendDocumentAsync_WhenServiceThrows_ShowsErrorDialogAndSetsBriefStatus()
    {
        // Arrange
        var testService = new MockErrorPdfService
        {
            ThrowOnAppend = new UnauthorizedAccessException("アクセスが拒否されました。")
        };
        var vm = new MainViewModel(pdfService: testService);
        var session = new DocumentSession(new PdfDocumentModel());
        session.Document.AddPage(new PdfPageModel());
        vm.Documents.Add(session);
        vm.ActiveSession = session;

        string? promptedTitle = null;
        string? promptedMessage = null;
        vm.ShowErrorPrompt = (title, message) =>
        {
            promptedTitle = title;
            promptedMessage = message;
        };

        // Act: 結合を実行
        await vm.AppendDocumentAsync(@"C:\Test\Protected.pdf");

        // Assert: 結合エラーダイアログが表示され、簡潔なステータスが設定されること
        Assert.True(vm.IsErrorDialogVisible);
        Assert.Equal("PDF結合エラー", vm.ErrorDialogTitle);
        Assert.Contains("アクセスが拒否されました。", vm.ErrorDialogMessage);
        Assert.Equal("PDF結合エラー", promptedTitle);
        Assert.Equal("結合に失敗しました。", vm.StatusMessage);
    }

    /// <summary>
    /// エラー発生シミュレーション用のモックPdfService
    /// </summary>
    private class MockErrorPdfService : IPdfService
    {
        public Exception? ThrowOnSave { get; set; }
        public Exception? ThrowOnLoad { get; set; }
        public Exception? ThrowOnAppend { get; set; }

        public Task AppendDocumentAsync(PdfDocumentModel targetDoc, string filePath, int insertIndex = -1)
        {
            if (ThrowOnAppend != null) throw ThrowOnAppend;
            return Task.CompletedTask;
        }

        public PdfPageModel CreateBlankPage(double width = 595.28, double height = 841.89) => new();

        public Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputPath) => Task.CompletedTask;

        public Task<PdfDocumentModel> LoadDocumentAsync(string filePath)
        {
            if (ThrowOnLoad != null) throw ThrowOnLoad;
            return Task.FromResult(new PdfDocumentModel());
        }

        public Task SaveDocumentAsync(PdfDocumentModel doc, string outputPath)
        {
            if (ThrowOnSave != null) throw ThrowOnSave;
            return Task.CompletedTask;
        }

        public Task<int> SplitAllPagesAsync(PdfDocumentModel doc, string outputDirectory, string baseFileName) => Task.FromResult(0);

        public Task<List<PdfPageModel>> SplitPagesHalfAsync(IEnumerable<PdfPageModel> pages, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<PdfPageModel>());
    }
}
