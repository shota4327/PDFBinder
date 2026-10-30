using System.IO;
using PDFBinder.App;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests.ViewModels;

/// <summary>
/// 複数ファイルのオープン・切り替え・個別終了に関する単体テストクラス (Issue #51)
/// </summary>
public class MainViewModelMultiFileTests
{
    private class MockMultiPdfService : IPdfService
    {
        public List<string> OpenedPaths { get; } = new();
        public List<string> SavedPaths { get; } = new();

        public Task<PdfDocumentModel> LoadDocumentAsync(string filePath)
        {
            OpenedPaths.Add(filePath);
            var doc = new PdfDocumentModel
            {
                FilePath = filePath
            };
            int pageCount = filePath.Contains("Large") ? 300 : 1;
            for (int i = 0; i < pageCount; i++)
            {
                doc.AddPage(new PdfPageModel { SourceFilePath = filePath, Width = 500, Height = 700 });
            }
            doc.IsModified = false;
            return Task.FromResult(doc);
        }

        public Task AppendDocumentAsync(PdfDocumentModel targetDocument, string sourceFilePath, int insertIndex = -1)
        {
            targetDocument.AddPage(new PdfPageModel { SourceFilePath = sourceFilePath });
            return Task.CompletedTask;
        }

        public Task SaveDocumentAsync(PdfDocumentModel document, string targetFilePath)
        {
            SavedPaths.Add(targetFilePath);
            document.FilePath = targetFilePath;
            document.IsModified = false;
            return Task.CompletedTask;
        }

        public Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputFilePath) => Task.CompletedTask;
        public Task<int> SplitAllPagesAsync(PdfDocumentModel document, string outputDirectory, string baseFileName) => Task.FromResult(document.PageCount);
        public Task<List<PdfPageModel>> SplitPagesHalfAsync(IEnumerable<PdfPageModel> pages, CancellationToken cancellationToken = default) => Task.FromResult(new List<PdfPageModel>());

        public PdfPageModel CreateBlankPage(double width, double height)
        {
            return new PdfPageModel
            {
                Width = width,
                Height = height
            };
        }
    }

    [Fact]
    public async Task OpenDocument_WhenMultipleFiles_CreatesSeparateSessionsAndIncreasesDropdownCount()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        // Act
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");

        // Assert
        Assert.Equal(2, vm.Documents.Count);
        Assert.NotNull(vm.ActiveSession);
        Assert.Equal("DocB.pdf", vm.ActiveSession.Document.FileName);
        Assert.True(vm.HasOpenDocuments);
        Assert.Equal("DocB.pdf", vm.DisplayFileName);
    }

    [Fact]
    public async Task OpenDocument_WhenDuplicateFilePath_ActivatesExistingSessionWithoutDuplicating()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        Assert.Equal(2, vm.Documents.Count);
        Assert.Equal("DocB.pdf", vm.ActiveSession?.Document.FileName);

        // Act - DocA を再度開く
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");

        // Assert - セッション数は増えず、DocAがアクティブになる
        Assert.Equal(2, vm.Documents.Count);
        Assert.Equal("DocA.pdf", vm.ActiveSession?.Document.FileName);
    }

    [Fact]
    public async Task SwitchDocument_ChangesActiveSessionAndRestoresState()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        sessionA.IsDetailViewActive = true;
        sessionA.ZoomFactor = 1.5;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        var sessionB = vm.ActiveSession!;
        sessionB.IsDetailViewActive = false;

        // Act
        vm.SwitchDocument(sessionA);

        // Assert
        Assert.Equal(sessionA, vm.ActiveSession);
        Assert.True(vm.IsDetailViewActive);
        Assert.Equal("DocA.pdf", vm.DisplayFileName);
    }

    [Fact]
    public async Task IndependentUndoRedo_EditsInOneDocument_DoNotPolluteOtherDocumentHistory()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        var sessionB = vm.ActiveSession!;

        // Act 1: DocB で白紙追加（1操作実行）
        vm.AddBlankPage();
        Assert.True(vm.CanUndo);

        // Act 2: DocA に切り替え
        vm.SwitchDocument(sessionA);

        // Assert: DocA は何も操作していないため CanUndo が false
        Assert.False(vm.CanUndo);

        // Act 3: 再び DocB に切り替え
        vm.SwitchDocument(sessionB);

        // Assert: DocB は CanUndo が true のまま
        Assert.True(vm.CanUndo);

        // Act 4: 元に戻す実行
        vm.Undo();
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);
    }

    [Fact]
    public void AddBlankPage_WhenNoDocumentOpen_CreatesUntitledPdfAndSetsActive()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);
        Assert.Equal(string.Empty, vm.DisplayFileName);

        // Act
        vm.AddBlankPage();

        // Assert
        Assert.Single(vm.Documents);
        Assert.NotNull(vm.ActiveSession);
        Assert.Equal("名称未設定.pdf", vm.ActiveSession.Document.FileName);
        Assert.Equal("名称未設定.pdf", vm.DisplayFileName);
        Assert.Equal(1, vm.ActiveSession.Document.PageCount);
        Assert.True(vm.HasOpenDocuments);
    }

    [Fact]
    public async Task CloseDocument_WhenNotModified_RemovesSessionAndActivatesAdjacent()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocC.pdf");

        var sessionB = vm.Documents[1];
        vm.SwitchDocument(sessionB);
        Assert.Equal("DocB.pdf", vm.ActiveSession?.Document.FileName);

        // Act: アクティブな DocB を閉じる
        bool closed = await vm.CloseDocumentAsync(sessionB);

        // Assert
        Assert.True(closed);
        Assert.Equal(2, vm.Documents.Count);
        // 隣接するセッション（インデックス1にある DocC）がアクティブ化
        Assert.Equal("DocC.pdf", vm.ActiveSession?.Document.FileName);
    }

    [Fact]
    public async Task CloseDocument_WhenModified_PromptsSave_Cancel_AbortsClosing()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        sessionA.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Cancel;

        // Act
        bool closed = await vm.CloseDocumentAsync(sessionA);

        // Assert
        Assert.False(closed);
        Assert.Single(vm.Documents);
        Assert.Equal(sessionA, vm.ActiveSession);
    }

    [Fact]
    public async Task CloseDocument_WhenModified_PromptsSave_Save_SavesAndCloses()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        sessionA.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Save;

        // Act
        bool closed = await vm.CloseDocumentAsync(sessionA);

        // Assert
        Assert.True(closed);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);
        Assert.Single(service.SavedPaths);
        Assert.Equal(@"C:\Folder\DocA.pdf", service.SavedPaths[0]);
    }

    [Fact]
    public async Task CloseDocument_WhenAllDocumentsClosed_TransitionsToEmptyState()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        Assert.Single(vm.Documents);

        // Act
        bool closed = await vm.CloseDocumentAsync(vm.ActiveSession);

        // Assert
        Assert.True(closed);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);
        Assert.False(vm.HasOpenDocuments);
        Assert.Equal(string.Empty, vm.DisplayFileName);
        Assert.Equal(0, vm.Document.PageCount);
    }

    [Fact]
    public async Task ConfirmSaveAllAsync_WhenMultipleModifiedDocuments_PromptsSequentially()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        var promptedFiles = new List<string>();
        vm.ConfirmSavePrompt = fileName =>
        {
            promptedFiles.Add(fileName);
            return SaveConfirmationResult.Save;
        };

        // Act
        bool canClose = await vm.ConfirmSaveAllAsync();

        // Assert
        Assert.True(canClose);
        Assert.Equal(2, promptedFiles.Count);
        Assert.Contains("DocA.pdf", promptedFiles);
        Assert.Contains("DocB.pdf", promptedFiles);
        Assert.Equal(2, service.SavedPaths.Count);
    }

    [Fact]
    public async Task ConfirmSaveAllAsync_WhenUserCancels_AbortsAndReturnsFalse()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        vm.ActiveSession!.Document.IsModified = true;

        vm.ConfirmSavePrompt = _ => SaveConfirmationResult.Cancel;

        // Act
        bool canClose = await vm.ConfirmSaveAllAsync();

        // Assert
        Assert.False(canClose);
        Assert.Empty(service.SavedPaths);
    }

    [Fact]
    public async Task DisplayTitle_WhenModified_DisplaysAsterisk()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var session = vm.ActiveSession!;

        // Assert - 未変更時
        Assert.Equal("DocA.pdf", session.DisplayTitle);

        // Act - 変更あり
        session.Document.IsModified = true;

        // Assert - 変更後
        Assert.Equal("DocA.pdf *", session.DisplayTitle);
    }

    [Fact]
    public void CalculateFileDropdownPopupPlacement_CentersPopupHorizontallyBelowTarget()
    {
        // Arrange: トグルボタン幅 160, 高さ 32, ポップアップ幅 320, 高さ 200
        var targetSize = new System.Windows.Size(160, 32);
        var popupSize = new System.Windows.Size(320, 200);

        // Act
        var placements = MainWindow.CalculateFileDropdownPopupPlacement(popupSize, targetSize, new System.Windows.Point(0, 0));

        // Assert
        Assert.Single(placements);
        // x は (160 - 320) / 2 = -80 (ポップアップの中心がトグルボタンの中心 80 に一致)
        Assert.Equal(-80.0, placements[0].Point.X);
        Assert.Equal(34.0, placements[0].Point.Y);
        Assert.Equal(System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal, placements[0].PrimaryAxis);
    }

    [Fact]
    public async Task OpenSecondDocument_WithFitToWindow_MaintainsFitToWindowAndCalculatesFitZoom()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);
        vm.DetailEditor!.UpdateViewportSize(1000, 800);

        // Act 1: 1つ目のファイルを開く
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        Assert.Equal(DetailViewFitMode.FitToWindow, vm.DetailEditor.FitMode);
        double fitZoomA = vm.DetailEditor.Zoom;
        Assert.NotEqual(1.0, fitZoomA);

        // Act 2: 2つ目のファイルを開く（別ページサイズ）
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");

        // Assert: 2つ目のファイルでも FitToWindow が維持され、1.0 に上書きされないこと
        Assert.Equal(DetailViewFitMode.FitToWindow, vm.DetailEditor.FitMode);
        Assert.Equal(vm.DetailEditor.FitMode, vm.ActiveSession!.FitMode);
        Assert.NotEqual(1.0, vm.DetailEditor.Zoom);
        Assert.Equal(fitZoomA, vm.DetailEditor.Zoom);
    }

    [Fact]
    public async Task SwitchDocument_WithManualZoom_RestoresManualZoomWhenFitModeIsNone()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);
        vm.DetailEditor!.UpdateViewportSize(1000, 800);

        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;

        // 手動ズーム（200%）に設定 -> FitMode は None になる
        vm.DetailEditor.SetZoom(2.0);
        vm.DetailEditor.FitMode = DetailViewFitMode.None;
        Assert.Equal(2.0, vm.DetailEditor.Zoom);

        // 2つ目のファイルを開く
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        var sessionB = vm.ActiveSession!;
        Assert.Equal(DetailViewFitMode.FitToWindow, sessionB.FitMode);

        // Act: DocA に切り替え
        vm.SwitchDocument(sessionA);

        // Assert: DocA の手動ズーム（2.0）および FitMode.None が復元されること
        Assert.Equal(DetailViewFitMode.None, vm.DetailEditor.FitMode);
        Assert.Equal(2.0, vm.DetailEditor.Zoom);
    }

    [Fact]
    public async Task SwitchDocument_WhenLargeDocumentWithCurrentPageNumber_DirectlyInitializesToTargetPage()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        // 1ページのファイルを開く
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;

        // 300ページのファイルを開く
        await vm.OpenSingleDocumentAsync(@"C:\Folder\LargeDocB.pdf");
        var sessionB = vm.ActiveSession!;
        Assert.Equal(300, sessionB.Document.PageCount);

        // 200ページ目に移動
        sessionB.CurrentPageNumber = 200;
        vm.DetailEditor!.CurrentPageNumber = 200;
        Assert.Equal(200, vm.DetailEditor.CurrentPageNumber);

        // Act 1: DocA に切り替え
        vm.SwitchDocument(sessionA);
        Assert.Equal(1, vm.DetailEditor.CurrentPageNumber);

        // Act 2: LargeDocB に切り替え
        vm.SwitchDocument(sessionB);

        // Assert: 1ページ目を経由せず直接200ページ目で初期化・復元されていること
        Assert.Equal(200, vm.DetailEditor.CurrentPageNumber);
        Assert.NotNull(vm.DetailEditor.CurrentPage);
        Assert.Equal(200, vm.DetailEditor.CurrentPage.PageNumber);
    }

    [Fact]
    public async Task OpenCloseAndReopenSameDocument_CancelsDetailRenderAndReopensSafely()
    {
        // Arrange: 300ページの大容量ドキュメント
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        // Act 1: 1回目のオープン
        await vm.OpenSingleDocumentAsync(@"C:\Folder\LargeDocB.pdf");
        Assert.Single(vm.Documents);
        var session = vm.ActiveSession!;

        // Act 2: タブを閉じる
        bool closed = await vm.CloseDocumentAsync(session);
        Assert.True(closed);
        Assert.Empty(vm.Documents);
        Assert.Null(vm.ActiveSession);

        // Act 3: 再び同じファイルを再オープン
        await vm.OpenSingleDocumentAsync(@"C:\Folder\LargeDocB.pdf");

        // Assert: 安全に再オープンされ、セッションとドキュメントが正しく復元されていること
        Assert.Single(vm.Documents);
        Assert.NotNull(vm.ActiveSession);
        Assert.Equal("LargeDocB.pdf", vm.ActiveSession.Document.FileName);
        Assert.Equal(300, vm.ActiveSession.Document.PageCount);
    }

    [Fact]
    public async Task SwitchDocument_WhenWindowSizeChanged_RecalculatesZoomForFitModes_AndPreservesActualSizeAndCustomZoom()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);
        vm.DetailEditor!.UpdateViewportSize(800, 600);

        // DocA (FitToWindow)
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        sessionA.FitMode = DetailViewFitMode.FitToWindow;
        vm.DetailEditor.FitMode = DetailViewFitMode.FitToWindow;
        vm.DetailEditor.ApplyFitMode();
        double initialZoomA = vm.DetailEditor.Zoom;

        // DocB (FitToWidth)
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        var sessionB = vm.ActiveSession!;
        sessionB.FitMode = DetailViewFitMode.FitToWidth;
        vm.DetailEditor.FitMode = DetailViewFitMode.FitToWidth;
        vm.DetailEditor.ApplyFitMode();
        double initialZoomB = vm.DetailEditor.Zoom;

        // DocC (ActualSize: 100%)
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocC.pdf");
        var sessionC = vm.ActiveSession!;
        sessionC.FitMode = DetailViewFitMode.ActualSize;
        vm.DetailEditor.FitMode = DetailViewFitMode.ActualSize;
        vm.DetailEditor.ApplyFitMode();
        Assert.Equal(1.0, vm.DetailEditor.Zoom);

        // DocD (None: 手動カスタム倍率 1.75)
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocD.pdf");
        var sessionD = vm.ActiveSession!;
        sessionD.FitMode = DetailViewFitMode.None;
        sessionD.ZoomFactor = 1.75;
        vm.DetailEditor.FitMode = DetailViewFitMode.None;
        vm.DetailEditor.Zoom = 1.75;
        Assert.Equal(1.75, vm.DetailEditor.Zoom);

        // Act & Assert 1: ウィンドウサイズ（ビューポート）を変更 (800x600 -> 1200x900)
        vm.DetailEditor.UpdateViewportSize(1200, 900);

        // DocA (FitToWindow) に切り替え -> 変更後のウィンドウサイズで拡大率が再計算されること
        vm.SwitchDocument(sessionA);
        Assert.Equal(DetailViewFitMode.FitToWindow, vm.DetailEditor.FitMode);
        Assert.True(vm.DetailEditor.Zoom > initialZoomA);

        // DocB (FitToWidth) に切り替え -> 変更後のウィンドウサイズで幅合わせが再計算されること
        vm.SwitchDocument(sessionB);
        Assert.Equal(DetailViewFitMode.FitToWidth, vm.DetailEditor.FitMode);
        Assert.True(vm.DetailEditor.Zoom > initialZoomB);

        // DocC (ActualSize) に切り替え -> ウィンドウサイズ変更後も 1.0 (100%) がそのまま保持されること
        vm.SwitchDocument(sessionC);
        Assert.Equal(DetailViewFitMode.ActualSize, vm.DetailEditor.FitMode);
        Assert.Equal(1.0, vm.DetailEditor.Zoom);

        // DocD (None / カスタム倍率 1.75) に切り替え -> ウィンドウサイズ変更後も 1.75 がそのまま保持されること
        vm.SwitchDocument(sessionD);
        Assert.Equal(DetailViewFitMode.None, vm.DetailEditor.FitMode);
        Assert.Equal(1.75, vm.DetailEditor.Zoom);
    }

    [Fact]
    public async Task SwitchDocument_PreservesDetailEditorInstances_AndKeepsBackgroundAndScrollOffsets()
    {
        // Arrange
        var service = new MockMultiPdfService();
        var vm = new MainViewModel(pdfService: service);

        // DocA: 連続表示、手動拡大 3.0 (300%)、スクロールオフセット
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocA.pdf");
        var sessionA = vm.ActiveSession!;
        var editorA = vm.DetailEditor!;
        editorA.PageViewMode = DetailPageViewMode.Continuous;
        editorA.SetZoom(3.0);
        editorA.SavedVerticalOffset = 1250.0;
        editorA.SavedHorizontalOffset = 300.0;
        var dummyBitmapA = System.Windows.Media.Imaging.BitmapSource.Create(
            10, 10, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null,
            new byte[10 * 10 * 4], 10 * 4);
        dummyBitmapA.Freeze();
        editorA.Pages[0].PageBackground = dummyBitmapA;

        // DocB: 単一表示、FitToWindow
        await vm.OpenSingleDocumentAsync(@"C:\Folder\DocB.pdf");
        var sessionB = vm.ActiveSession!;
        var editorB = vm.DetailEditor!;
        editorB.PageViewMode = DetailPageViewMode.SinglePage;
        editorB.SetFitMode(DetailViewFitMode.FitToWindow);

        Assert.NotSame(editorA, editorB);
        Assert.Same(editorB, vm.DetailEditor);
        Assert.NotNull(vm.DetailEditor);
        Assert.Equal(DetailPageViewMode.SinglePage, vm.DetailEditor!.PageViewMode);

        // Act 1: DocA に切り替え
        vm.SwitchDocument(sessionA);

        // Assert 1: DocA の ViewModel インスタンス、設定、キャッシュがすべて保持されていること
        Assert.Same(editorA, vm.DetailEditor);
        Assert.Equal(DetailPageViewMode.Continuous, vm.DetailEditor!.PageViewMode);
        Assert.Equal(DetailViewFitMode.None, vm.DetailEditor.FitMode);
        Assert.Equal(3.0, vm.DetailEditor.Zoom);
        Assert.Equal(1250.0, vm.DetailEditor.SavedVerticalOffset);
        Assert.Equal(300.0, vm.DetailEditor.SavedHorizontalOffset);
        Assert.NotNull(vm.DetailEditor.Pages[0].PageBackground);

        // Act 2: DocB に切り替え
        vm.SwitchDocument(sessionB);

        // Assert 2: DocB の状態が崩れず保持されていること
        Assert.Same(editorB, vm.DetailEditor);
        Assert.Equal(DetailPageViewMode.SinglePage, vm.DetailEditor.PageViewMode);
        Assert.Equal(DetailViewFitMode.FitToWindow, vm.DetailEditor.FitMode);

        // Act 3: DocA を閉じる
        await vm.CloseDocumentAsync(sessionA);

        // Assert 3: DocA が閉じられ、DocB がアクティブであること
        Assert.Single(vm.Documents);
        Assert.Same(sessionB, vm.ActiveSession);
        Assert.Same(editorB, vm.DetailEditor);
    }
}

