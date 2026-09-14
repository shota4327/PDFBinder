using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 未保存の変更状態（IsModified）および保存確認ダイアログの挙動に関する単体テスト
/// </summary>
public class UnsavedChangesTests
{
    [Fact]
    public void Document_WhenPageAdded_SetsIsModifiedToTrue()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        doc.ResetModifiedState();
        Assert.False(doc.IsModified);

        // Act
        doc.AddPage(new PdfPageModel());

        // Assert
        Assert.True(doc.IsModified);
    }

    [Fact]
    public void Document_WhenPageRemoved_SetsIsModifiedToTrue()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var page = new PdfPageModel();
        doc.AddPage(page);
        doc.ResetModifiedState();
        Assert.False(doc.IsModified);

        // Act
        doc.RemovePage(page);

        // Assert
        Assert.True(doc.IsModified);
    }

    [Fact]
    public void Document_WhenPageMoved_SetsIsModifiedToTrue()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        doc.AddPage(new PdfPageModel());
        doc.AddPage(new PdfPageModel());
        doc.ResetModifiedState();
        Assert.False(doc.IsModified);

        // Act
        doc.MovePage(0, 1);

        // Assert
        Assert.True(doc.IsModified);
    }

    [Fact]
    public void Document_WhenPageRotationChanged_SetsBothPageAndDocumentModified()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var page = new PdfPageModel();
        doc.AddPage(page);
        doc.ResetModifiedState();
        Assert.False(doc.IsModified);
        Assert.False(page.IsModified);

        // Act
        page.RotateClockwise();

        // Assert
        Assert.True(page.IsModified);
        Assert.True(page.IsThumbnailDirty);
        Assert.True(doc.IsModified);
    }

    [Fact]
    public void Document_WhenInkStrokesAdded_SetsBothPageAndDocumentModified()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var page = new PdfPageModel();
        doc.AddPage(page);
        doc.ResetModifiedState();
        Assert.False(doc.IsModified);
        Assert.False(page.IsModified);

        // Act
        var points = new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(20.0, 20.0) };
        page.InkStrokes.Add(new Stroke(points));

        // Assert
        Assert.True(page.IsModified);
        Assert.True(page.IsThumbnailDirty);
        Assert.True(doc.IsModified);
    }

    [Fact]
    public void Document_WhenInkStrokesErasedOrCleared_RemainsModified()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var page = new PdfPageModel();
        doc.AddPage(page);
        doc.ResetModifiedState();

        var points = new StylusPointCollection { new StylusPoint(10.0, 10.0), new StylusPoint(20.0, 20.0) };
        var stroke = new Stroke(points);
        page.InkStrokes.Add(stroke);
        Assert.True(doc.IsModified);

        // Act: 書いたストロークをすべて削除
        page.InkStrokes.Clear();

        // Assert: 一度でも編集があれば未保存変更ありのまま
        Assert.True(page.IsModified);
        Assert.True(doc.IsModified);
    }

    [Fact]
    public void Document_ResetModifiedState_ClearsModifiedAndThumbnailDirtyFlags()
    {
        // Arrange
        var doc = new PdfDocumentModel();
        var page = new PdfPageModel();
        doc.AddPage(page);
        page.RotateClockwise();
        Assert.True(doc.IsModified);
        Assert.True(page.IsModified);
        Assert.True(page.IsThumbnailDirty);

        // Act
        doc.ResetModifiedState();

        // Assert
        Assert.False(doc.IsModified);
        Assert.False(page.IsModified);
        Assert.False(page.IsThumbnailDirty);
    }

    [Fact]
    public async Task ConfirmSaveAndProceedAsync_WhenNotModified_ReturnsTrueWithoutPrompt()
    {
        // Arrange
        var vm = new MainViewModel();
        bool promptCalled = false;
        vm.ConfirmSavePrompt = _ =>
        {
            promptCalled = true;
            return SaveConfirmationResult.Cancel;
        };

        // Act
        bool result = await vm.ConfirmSaveAndProceedAsync();

        // Assert
        Assert.True(result);
        Assert.False(promptCalled);
    }

    [Fact]
    public async Task ConfirmSaveAndProceedAsync_WhenModifiedAndCancelled_ReturnsFalse()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.Document.AddPage(new PdfPageModel());
        vm.Document.IsModified = true;

        bool promptCalled = false;
        vm.ConfirmSavePrompt = fileName =>
        {
            promptCalled = true;
            return SaveConfirmationResult.Cancel;
        };

        // Act
        bool result = await vm.ConfirmSaveAndProceedAsync();

        // Assert
        Assert.False(result);
        Assert.True(promptCalled);
    }

    [Fact]
    public async Task ConfirmSaveAndProceedAsync_WhenModifiedAndDiscarded_ReturnsTrue()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.Document.AddPage(new PdfPageModel());
        vm.Document.IsModified = true;

        bool promptCalled = false;
        vm.ConfirmSavePrompt = fileName =>
        {
            promptCalled = true;
            return SaveConfirmationResult.Discard;
        };

        // Act
        bool result = await vm.ConfirmSaveAndProceedAsync();

        // Assert
        Assert.True(result);
        Assert.True(promptCalled);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenModifiedAndCancelled_DoesNotOpenNewFile()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel();
        vm.Document.AddPage(page);
        vm.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Cancel;

        // Act
        await vm.OpenDocumentAsync(@"C:\FakePath\NewDoc.pdf");

        // Assert: キャンセルされたため、ドキュメントは差し替えられずそのまま残る
        Assert.Contains(page, vm.Document.Pages);
    }

    [Fact]
    public async Task EnsureThumbnailsGeneratedAsync_ClearsThumbnailDirty_PreservesIsModified()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel();
        vm.Document.AddPage(page);
        page.RotateClockwise();
        Assert.True(page.IsModified);
        Assert.True(page.IsThumbnailDirty);
        Assert.True(vm.Document.IsModified);

        // Act
        await vm.EnsureThumbnailsGeneratedAsync();

        // Assert: サムネイルDirtyフラグはクリアされるが、未保存変更フラグは維持される
        Assert.False(page.IsThumbnailDirty);
        Assert.True(page.IsModified);
        Assert.True(vm.Document.IsModified);
    }

    [Fact]
    public async Task ConfirmSaveAndProceedAsync_WhenModifiedAndSaved_ReturnsTrueAndSaves()
    {
        // Arrange
        var testService = new TestPdfService();
        var vm = new MainViewModel(pdfService: testService);
        vm.Document.AddPage(new PdfPageModel());
        vm.Document.FilePath = @"C:\Fake\Doc.pdf";
        vm.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Save;

        // Act
        bool result = await vm.ConfirmSaveAndProceedAsync();

        // Assert
        Assert.True(result);
        Assert.True(testService.SaveCalled);
    }

    [Fact]
    public async Task ConfirmSaveAndProceedAsync_WhenSaveThrows_ReturnsFalse()
    {
        // Arrange
        var testService = new TestPdfService { ThrowOnSave = true };
        var vm = new MainViewModel(pdfService: testService);
        vm.Document.AddPage(new PdfPageModel());
        vm.Document.FilePath = @"C:\Fake\Doc.pdf";
        vm.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Save;

        // Act
        bool result = await vm.ConfirmSaveAndProceedAsync();

        // Assert: 保存失敗時は処理中断（false）
        Assert.False(result);
    }

    [Fact]
    public async Task PromptSaveConfirmationAsync_DisplaysOverlayAndResolvesOnConfirmSave()
    {
        // Arrange
        var vm = new MainViewModel();
        Assert.False(vm.IsSaveConfirmationVisible);

        // Act: 非同期で確認プロンプトを開始
        var promptTask = vm.PromptSaveConfirmationAsync("Sample.pdf");

        // Assert: オーバーレイが表示され、ファイル名が設定されている
        Assert.True(vm.IsSaveConfirmationVisible);
        Assert.Equal("Sample.pdf", vm.SaveConfirmationFileName);

        // Act: ユーザーが「保存しない」を選択
        vm.ConfirmSaveCommand.Execute(SaveConfirmationResult.Discard);
        var result = await promptTask;

        // Assert: 選択結果が正しく返却され、オーバーレイが閉じる
        Assert.Equal(SaveConfirmationResult.Discard, result);
        Assert.False(vm.IsSaveConfirmationVisible);
    }

    [Fact]
    public async Task CancelSaveConfirmation_WhenOverlayVisible_ResolvesAsCancel()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act: 確認プロンプトを開始し、キャンセル操作を実行
        var promptTask = vm.PromptSaveConfirmationAsync("Doc.pdf");
        Assert.True(vm.IsSaveConfirmationVisible);

        vm.CancelSaveConfirmation();
        var result = await promptTask;

        // Assert: キャンセルとして完了し、オーバーレイが非表示になる
        Assert.Equal(SaveConfirmationResult.Cancel, result);
        Assert.False(vm.IsSaveConfirmationVisible);
    }

    [Fact]
    public async Task ConfirmSaveAndProceedAsync_WithOverlayFlow_ResolvesCorrectly()
    {
        // Arrange
        var testService = new TestPdfService();
        var vm = new MainViewModel(pdfService: testService);
        vm.Document.AddPage(new PdfPageModel());
        vm.Document.FilePath = @"C:\Fake\Doc.pdf";
        vm.Document.IsModified = true;

        // Act: 確認処理を開始
        var proceedTask = vm.ConfirmSaveAndProceedAsync();
        Assert.True(vm.IsSaveConfirmationVisible);

        // ユーザーが「保存」ボタンをクリック
        vm.ConfirmSaveCommand.Execute(SaveConfirmationResult.Save);
        bool proceedResult = await proceedTask;

        // Assert: 保存が実行され、処理続行（true）が返る
        Assert.True(proceedResult);
        Assert.True(testService.SaveCalled);
        Assert.False(vm.IsSaveConfirmationVisible);
    }
}

/// <summary>
/// 単体テスト用のモックPdfService
/// </summary>
internal class TestPdfService : IPdfService
{
    public bool SaveCalled { get; private set; }
    public bool ThrowOnSave { get; set; }

    public Task AppendDocumentAsync(PdfDocumentModel targetDoc, string filePath, int insertIndex = -1) => Task.CompletedTask;
    public PdfPageModel CreateBlankPage(double width = 595.28, double height = 841.89) => new();
    public Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputPath) => Task.CompletedTask;
    public Task<PdfDocumentModel> LoadDocumentAsync(string filePath) => Task.FromResult(new PdfDocumentModel());
    public Task SaveDocumentAsync(PdfDocumentModel doc, string outputPath)
    {
        if (ThrowOnSave) throw new IOException("ディスクへの書き込みに失敗しました。");
        SaveCalled = true;
        doc.ResetModifiedState();
        return Task.CompletedTask;
    }
    public Task<int> SplitAllPagesAsync(PdfDocumentModel doc, string outputDirectory, string baseFileName) => Task.FromResult(0);
}
