using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 詳細ビューにおける白紙ページ追加時のナビゲーションおよび復元動作のテスト (Issue #163)
/// </summary>
public class DetailEditorBlankPageNavigationTests
{
    [Fact]
    public void AddBlankPage_WhenDetailViewActive_NavigatesToAddedBlankPage()
    {
        // Arrange
        var renderer = new PdfiumRenderer();
        var vm = new MainViewModel(pdfRenderer: renderer);
        vm.AddBlankPage();
        var initialPage = vm.Document.Pages[0];

        // 詳細エディタを開く
        vm.OpenPageDetail(initialPage);
        Assert.True(vm.IsDetailViewActive);
        Assert.NotNull(vm.DetailEditor);
        Assert.Equal(initialPage, vm.DetailEditor.CurrentPage);

        PdfPageModel? scrolledPage = null;
        vm.DetailEditor.ScrollToPageRequested += page => scrolledPage = page;

        // Act: 詳細ビュー表示中に白紙ページを追加
        vm.AddBlankPage();

        // Assert: 2ページ目が追加され、詳細エディタのカレントページが追加された白紙ページになっていること
        Assert.Equal(2, vm.Document.PageCount);
        var newPage = vm.Document.Pages[1];
        Assert.True(newPage.IsBlankPage);
        Assert.Equal(newPage, vm.DetailEditor.CurrentPage);
        Assert.Equal(newPage, scrolledPage);
    }

    [Fact]
    public void AddBlankPage_WhenDetailViewActive_Undo_RestoresPreviousPage()
    {
        // Arrange
        var renderer = new PdfiumRenderer();
        var vm = new MainViewModel(pdfRenderer: renderer);
        vm.AddBlankPage();
        var initialPage = vm.Document.Pages[0];

        vm.OpenPageDetail(initialPage);
        vm.AddBlankPage();

        Assert.Equal(2, vm.Document.PageCount);
        Assert.NotEqual(initialPage, vm.DetailEditor!.CurrentPage);

        // Act: 白紙ページ追加を取り消し (Undo)
        vm.Undo();

        // Assert: ページ数が1に戻り、カレントページが追加前の元のページに復元されていること
        Assert.Equal(1, vm.Document.PageCount);
        Assert.Equal(initialPage, vm.DetailEditor.CurrentPage);
    }

    [Fact]
    public void AddBlankPage_WhenGridViewActive_PreservesExistingSelection()
    {
        // Arrange
        var renderer = new PdfiumRenderer();
        var vm = new MainViewModel(pdfRenderer: renderer);
        vm.AddBlankPage();
        var initialPage = vm.Document.Pages[0];
        initialPage.IsSelected = true;

        // グリッドビューに切り替え
        vm.IsDetailViewActive = false;
        Assert.False(vm.IsDetailViewActive);

        // Act: グリッドビュー表示中に白紙ページを追加
        vm.AddBlankPage();

        // Assert: 元のページの選択状態が維持され、新規ページは未選択であること
        Assert.Equal(2, vm.Document.PageCount);
        Assert.True(initialPage.IsSelected);
        Assert.False(vm.Document.Pages[1].IsSelected);
    }

    [Fact]
    public void DetailEditorViewModel_InitializeDocument_WithPreferredPage_SelectsPreferredPage()
    {
        // Arrange
        var renderer = new PdfiumRenderer();
        var doc = new PdfDocumentModel();
        var page1 = new PdfPageModel();
        var page2 = new PdfPageModel();
        var page3 = new PdfPageModel();
        doc.AddPage(page1);
        doc.AddPage(page2);
        doc.AddPage(page3);

        var vm = new DetailEditorViewModel(page1, renderer, () => { }, _ => null);
        PdfPageModel? requestedScrollPage = null;
        vm.ScrollToPageRequested += p => requestedScrollPage = p;

        // Act: page3 を preferredPage として初期化
        vm.InitializeDocument(doc, page3);

        // Assert
        Assert.Equal(page3, vm.CurrentPage);
        Assert.Equal(page3, requestedScrollPage);
    }

    [Fact]
    public void DetailEditorViewModel_InitializeDocument_WhenPageDeleted_FallsBackToPreviousValidIndex()
    {
        // Arrange: 3ページある状態でインデックス2 (page3) を表示
        var renderer = new PdfiumRenderer();
        var doc = new PdfDocumentModel();
        var page1 = new PdfPageModel();
        var page2 = new PdfPageModel();
        var page3 = new PdfPageModel();
        doc.AddPage(page1);
        doc.AddPage(page2);
        doc.AddPage(page3);

        var vm = new DetailEditorViewModel(page1, renderer, () => { }, _ => null);
        vm.InitializeDocument(doc, page3);
        Assert.Equal(page3, vm.CurrentPage);
        Assert.Equal(2, vm.CurrentPageIndex);

        // Act: page3 が削除されたドキュメント（page1, page2 のみ）で初期化 (Undo等)
        var modifiedDoc = new PdfDocumentModel();
        modifiedDoc.AddPage(page1);
        modifiedDoc.AddPage(page2);

        vm.InitializeDocument(modifiedDoc);

        // Assert: 直前のインデックス1 (page2) にフォールバックすること
        Assert.Equal(page2, vm.CurrentPage);
        Assert.Equal(1, vm.CurrentPageIndex);
    }
}
