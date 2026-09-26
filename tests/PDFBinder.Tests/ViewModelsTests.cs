using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="MainViewModel"/> および <see cref="DetailEditorViewModel"/> の単体テスト
/// </summary>
public class ViewModelsTests
{
    [Fact]
    public void MainViewModel_AddBlankPage_InsertsPageAndEnablesUndo()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act
        vm.AddBlankPageCommand.Execute(null);

        // Assert
        Assert.Equal(1, vm.Document.PageCount);
        Assert.True(vm.Document.Pages[0].IsBlankPage);
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public void MainViewModel_RotateClockwise_RotatesPage()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.Document.Pages[0].IsSelected = true;

        // Act
        vm.RotateClockwiseCommand.Execute(null);

        // Assert
        Assert.Equal(PageRotation.Rotate90, vm.Document.Pages[0].Rotation);

        // Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(PageRotation.Rotate0, vm.Document.Pages[0].Rotation);
    }

    [Fact]
    public void MainViewModel_DeleteSelectedPages_RemovesPages()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.AddBlankPage();
        Assert.Equal(2, vm.Document.PageCount);

        vm.Document.Pages[0].IsSelected = true;

        // Act
        vm.DeleteSelectedPagesCommand.Execute(null);

        // Assert
        Assert.Equal(1, vm.Document.PageCount);

        // Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
    }

    [Fact]
    public void MainViewModel_OpenAndClosePageDetail_TogglesActiveState()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        var page = vm.Document.Pages[0];

        // Act
        vm.OpenPageDetailCommand.Execute(page);

        // Assert
        Assert.True(vm.IsDetailViewActive);
        Assert.NotNull(vm.DetailEditor);
        Assert.Equal(page, vm.DetailEditor.CurrentPage);

        // Close
        vm.ClosePageDetailCommand.Execute(null);
        Assert.False(vm.IsDetailViewActive);
    }

    [Fact]
    public void DetailEditorViewModel_SelectTool_ConfiguresToolDefaults()
    {
        // Arrange
        var page = new PdfPageModel();
        var renderer = new PdfiumRenderer();
        var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Act: Highlighter
        vm.SelectToolCommand.Execute(EditorToolMode.Highlighter);
        Assert.Equal(EditorToolMode.Highlighter, vm.SelectedTool);
        Assert.Equal(12.0, vm.StrokeThickness);
        Assert.Equal(DetailEditorViewModel.YellowPresetColor, vm.SelectedColor);

        // Act: Pen
        vm.SelectToolCommand.Execute(EditorToolMode.Pen);
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(1.0, vm.StrokeThickness);
        Assert.Equal(Colors.Black, vm.SelectedColor);
    }

    [Fact]
    public void DetailEditorViewModel_ZoomControls_WorkCorrectly()
    {
        // Arrange
        var page = new PdfPageModel();
        var renderer = new PdfiumRenderer();
        var vm = new DetailEditorViewModel(page, renderer, () => { }, _ => null);

        // Zoom in
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.25, vm.Zoom);

        // Zoom out
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(1.0, vm.Zoom);

        // Reset
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        vm.ZoomResetCommand.Execute(null);
        Assert.Equal(1.0, vm.Zoom);

        // Direct SetZoom
        vm.SetZoom(1.75);
        Assert.Equal(1.75, vm.Zoom);

        // Direct SetZoom Clamp Max
        vm.SetZoom(5.0);
        Assert.Equal(5.0, vm.Zoom);
        vm.SetZoom(40.0);
        Assert.Equal(DetailEditorViewModel.MaxZoom, vm.Zoom);

        // Direct SetZoom Clamp Min
        vm.SetZoom(0.1);
        Assert.Equal(DetailEditorViewModel.MinZoom, vm.Zoom);
    }

    [Fact]
    public void MainViewModel_ZoomThumbnailCommands_WorkAndClampCorrectly()
    {
        // Arrange
        var vm = new MainViewModel();

        // Assert default
        Assert.Equal(220.0, vm.ThumbnailSize);
        Assert.True(vm.CanZoomInThumbnail);
        Assert.True(vm.CanZoomOutThumbnail);
        Assert.True(vm.ZoomInThumbnailCommand.CanExecute(null));
        Assert.True(vm.ZoomOutThumbnailCommand.CanExecute(null));

        // Act: Zoom in once (220 -> 275: 100% -> 125%)
        vm.ZoomInThumbnailCommand.Execute(null);
        Assert.Equal(275.0, vm.ThumbnailSize);

        // Act: Zoom in to maximum (7040.0)
        while (vm.CanZoomInThumbnail)
        {
            vm.ZoomInThumbnailCommand.Execute(null);
        }
        Assert.Equal(MainViewModel.MaxThumbnailSize, vm.ThumbnailSize);
        Assert.False(vm.CanZoomInThumbnail);
        Assert.False(vm.ZoomInThumbnailCommand.CanExecute(null));

        // Ensure does not exceed maximum
        vm.ZoomInThumbnailCommand.Execute(null);
        Assert.Equal(MainViewModel.MaxThumbnailSize, vm.ThumbnailSize);

        // Act: Zoom out to minimum (110.0)
        while (vm.CanZoomOutThumbnail)
        {
            vm.ZoomOutThumbnailCommand.Execute(null);
        }
        Assert.Equal(MainViewModel.MinThumbnailSize, vm.ThumbnailSize);
        Assert.False(vm.CanZoomOutThumbnail);
        Assert.False(vm.ZoomOutThumbnailCommand.CanExecute(null));

        // Ensure does not fall below minimum
        vm.ZoomOutThumbnailCommand.Execute(null);
        Assert.Equal(MainViewModel.MinThumbnailSize, vm.ThumbnailSize);

        // Act: Zoom in from minimum re-enables zoom out (110 -> 165: 50% -> 75%)
        vm.ZoomInThumbnailCommand.Execute(null);
        Assert.Equal(165.0, vm.ThumbnailSize);
        Assert.True(vm.CanZoomOutThumbnail);
        Assert.True(vm.ZoomOutThumbnailCommand.CanExecute(null));
    }

    [Fact]
    public void DetailEditorViewModel_SelectedTool_Changed_UpdatesColorAndThickness()
    {
        // Arrange
        var page = new PdfPageModel();
        var vm = new DetailEditorViewModel(
            page,
            new PDFBinder.Core.Services.PdfiumRenderer(),
            () => { },
            _ => null);

        // 初期状態（移動ツール）の確認
        Assert.Equal(EditorToolMode.Hand, vm.SelectedTool);

        // Act: 通常ペンに変更
        vm.SelectedTool = EditorToolMode.Pen;

        // Assert: 色が黒色、太さが1pxであることを確認
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(Colors.Black, vm.SelectedColor);
        Assert.Equal(1.0, vm.StrokeThickness);

        // Act: 蛍光ペンに変更（TwoWayバインディング経由のプロパティ直接変更）
        vm.SelectedTool = EditorToolMode.Highlighter;

        // Assert: 色が黄色、太さが12pxに自動調整される
        Assert.Equal(EditorToolMode.Highlighter, vm.SelectedTool);
        Assert.Equal(DetailEditorViewModel.YellowPresetColor, vm.SelectedColor);
        Assert.Equal(12.0, vm.StrokeThickness);

        // Act: 再び通常ペンに変更
        vm.SelectedTool = EditorToolMode.Pen;

        // Assert: 色が黒色、太さが1pxに復帰する
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(Colors.Black, vm.SelectedColor);
        Assert.Equal(1.0, vm.StrokeThickness);

        // Act: 全消しゴムに変更
        vm.SelectedTool = EditorToolMode.EraserStroke;

        // Assert: ツールが正しく切り替わる
        Assert.Equal(EditorToolMode.EraserStroke, vm.SelectedTool);
    }

    [Fact]
    public void MainViewModel_RibbonTab_SwitchesAutomaticallyOnDetailEditorOpenAndClose()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        var page = vm.Document.Pages[0];

        // Assert: 初期状態は「PDF編集」タブ（0）
        Assert.Equal(0, vm.SelectedRibbonTabIndex);

        // Act: エディタを開く
        vm.OpenPageDetailCommand.Execute(page);

        // Assert: 「手書き」タブ（1）へ自動切り替え
        Assert.Equal(1, vm.SelectedRibbonTabIndex);

        // Act: エディタを閉じる（グリッドビューへ移行）
        vm.ClosePageDetailCommand.Execute(null);

        // Assert: 「表示」タブ（2）へ自動切り替え（手書きタブ無効化に伴う遷移）
        Assert.Equal(2, vm.SelectedRibbonTabIndex);
    }

    [Fact]
    public void DetailEditorViewModel_PresetThickness_WorksCorrectly()
    {
        // Arrange
        var page = new PdfPageModel();
        var vm = new DetailEditorViewModel(
            page,
            new PDFBinder.Core.Services.PdfiumRenderer(),
            () => { },
            _ => null);

        // 初期太さ
        Assert.Equal(1.0, vm.StrokeThickness);

        // Act: プリセット選択（0.5px）
        vm.SetPresetThicknessCommand.Execute(0.5);
        Assert.Equal(0.5, vm.StrokeThickness);

        // Act: プリセット選択（1.0px）
        vm.SetPresetThicknessCommand.Execute(1.0);
        Assert.Equal(1.0, vm.StrokeThickness);

        // Act: 文字列引数（"4.0"）でのプリセット選択
        vm.SetPresetThicknessCommand.Execute("4.0");
        Assert.Equal(4.0, vm.StrokeThickness);
    }

    [Fact]
    public void DetailEditorViewModel_ColorPalette_ContainsFiveModernColors()
    {
        // Arrange
        var page = new PdfPageModel();
        var vm = new DetailEditorViewModel(
            page,
            new PDFBinder.Core.Services.PdfiumRenderer(),
            () => { },
            _ => null);

        // Assert: 5色（黒、赤、青、緑、黄）
        Assert.Equal(5, vm.ColorPalette.Count);
        Assert.Equal(Color.FromRgb(0x00, 0x00, 0x00), vm.ColorPalette[0]);
        Assert.Equal(Color.FromRgb(0xEF, 0x44, 0x44), vm.ColorPalette[1]);
        Assert.Equal(Color.FromRgb(0x25, 0x63, 0xEB), vm.ColorPalette[2]);
        Assert.Equal(Color.FromRgb(0x16, 0xA3, 0x4A), vm.ColorPalette[3]);
        Assert.Equal(DetailEditorViewModel.YellowPresetColor, vm.ColorPalette[4]);
    }

    [Fact]
    public void DetailEditorViewModel_SelectColorCommand_SelectsYellow()
    {
        // Arrange
        var page = new PdfPageModel();
        var vm = new DetailEditorViewModel(
            page,
            new PDFBinder.Core.Services.PdfiumRenderer(),
            () => { },
            _ => null);

        // Act
        vm.SelectColorCommand.Execute(DetailEditorViewModel.YellowPresetColor);

        // Assert
        Assert.Equal(DetailEditorViewModel.YellowPresetColor, vm.SelectedColor);
    }

    [Fact]
    public void DetailEditorViewModel_EditorRenderScale_IsConfiguredProperly()
    {
        // Assert: 216 DPI相当（3.0倍）に設定されていること
        Assert.Equal(3.0, DetailEditorViewModel.EditorRenderScale);
    }

    [Fact]
    public void MainViewModel_ThumbnailRenderConstants_AreConfiguredProperly()
    {
        // Assert: 高解像度（720px）基準でレンダリング定数が設定されていること
        Assert.Equal(720, MainViewModel.ThumbnailRenderWidth);
        Assert.Equal(1008, MainViewModel.ThumbnailRenderHeight);
    }

    [Fact]
    public void MainViewModel_ClosePageDetail_WithStrokes_UpdatesThumbnailWithStrokes()
    {
        // Arrange
        var renderer = new PdfiumRenderer();
        var vm = new MainViewModel(pdfRenderer: renderer);
        vm.AddBlankPage();
        var page = vm.Document.Pages[0];
        var initialThumbnail = page.Thumbnail;

        // Act: 詳細エディタを開いてストロークを追加し、詳細エディタを閉じる
        vm.OpenPageDetail(page);
        var points = new StylusPointCollection
        {
            new StylusPoint(10, 10),
            new StylusPoint(50, 50)
        };
        page.InkStrokes.Add(new Stroke(points));

        vm.ClosePageDetail();

        // Assert: サムネイルが手書きストローク合成後の新しいBitmapSourceに更新されていること
        Assert.NotNull(page.Thumbnail);
        Assert.NotSame(initialThumbnail, page.Thumbnail);
    }

    [Fact]
    public void MainViewModel_ShowPrintDialog_WhenNoPages_CannotExecute()
    {
        var vm = new MainViewModel();

        Assert.False(vm.CanExecutePrint);
        Assert.False(vm.ShowPrintDialogCommand.CanExecute(null));
    }

    [Fact]
    public void MainViewModel_ShowPrintDialog_WithPages_OpensDialog()
    {
        var vm = new MainViewModel();
        vm.AddBlankPage();

        Assert.True(vm.CanExecutePrint);
        Assert.True(vm.ShowPrintDialogCommand.CanExecute(null));

        vm.ShowPrintDialogCommand.Execute(null);

        Assert.True(vm.IsPrintDialogVisible);
        Assert.NotNull(vm.PrintViewModel);

        // キャンセルで閉じる
        vm.ClosePrintDialogCommand.Execute(null);
        Assert.False(vm.IsPrintDialogVisible);
        Assert.Null(vm.PrintViewModel);
    }

    [Fact]
    public void MainViewModel_MovePage_InGridView_MarksDetailEditorDirty_AndSyncsOnSwitchToDetailView()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.AddBlankPage();
        vm.AddBlankPage();
        var pageA = vm.Document.Pages[0];
        var pageB = vm.Document.Pages[1];
        var pageC = vm.Document.Pages[2];

        // 初期状態で詳細ビューからグリッドビューへ遷移
        vm.IsDetailViewActive = false;
        Assert.False(vm.IsDetailEditorDirty);

        // Act: グリッドビューでページ0を末尾（インデックス2）へ移動
        vm.MovePage(0, 2);

        // Assert: ドキュメントの並び順が更新され、詳細エディタに要再同期フラグが立っていること
        Assert.Equal(pageB, vm.Document.Pages[0]);
        Assert.Equal(pageC, vm.Document.Pages[1]);
        Assert.Equal(pageA, vm.Document.Pages[2]);
        Assert.True(vm.IsDetailEditorDirty);

        // Act: 詳細ビューへ戻る（pageAを開く）
        vm.OpenPageDetailCommand.Execute(pageA);

        // Assert: 要再同期フラグが解除され、DetailEditor.Pagesの並び順が最新のDocumentと完全に同期していること
        Assert.False(vm.IsDetailEditorDirty);
        Assert.Equal(3, vm.DetailEditor!.Pages.Count);
        Assert.Equal(pageB, vm.DetailEditor.Pages[0].Page);
        Assert.Equal(pageC, vm.DetailEditor.Pages[1].Page);
        Assert.Equal(pageA, vm.DetailEditor.Pages[2].Page);
        Assert.Equal(pageA, vm.DetailEditor.CurrentPage);
    }

    [Fact]
    public void MainViewModel_DeleteSelectedPages_InGridView_MarksDetailEditorDirty_AndSyncsOnSwitchToDetailView()
    {
        // Arrange
        var vm = new MainViewModel();
        vm.AddBlankPage();
        vm.AddBlankPage();
        vm.AddBlankPage();
        var pageA = vm.Document.Pages[0];
        var pageB = vm.Document.Pages[1];
        var pageC = vm.Document.Pages[2];

        vm.IsDetailViewActive = false;
        Assert.False(vm.IsDetailEditorDirty);

        // Act: グリッドビューでpageBを選択して削除
        pageB.IsSelected = true;
        vm.DeleteSelectedPagesCommand.Execute(null);

        // Assert: ドキュメントから削除され、要再同期フラグが立っていること
        Assert.Equal(2, vm.Document.Pages.Count);
        Assert.True(vm.IsDetailEditorDirty);

        // Act: 詳細ビューへ切り替え
        vm.IsDetailViewActive = true;

        // Assert: 要再同期フラグが解除され、残存ページが正しく同期されていること
        Assert.False(vm.IsDetailEditorDirty);
        Assert.Equal(2, vm.DetailEditor!.Pages.Count);
        Assert.Equal(pageA, vm.DetailEditor.Pages[0].Page);
        Assert.Equal(pageC, vm.DetailEditor.Pages[1].Page);
    }
}
