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
        Assert.Null(vm.DetailEditor);
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
        Assert.Equal(Colors.Yellow, vm.SelectedColor);

        // Act: Pen
        vm.SelectToolCommand.Execute(EditorToolMode.Pen);
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(2.0, vm.StrokeThickness);
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

        // Act: Zoom in once
        vm.ZoomInThumbnailCommand.Execute(null);
        Assert.Equal(240.0, vm.ThumbnailSize);

        // Act: Zoom in to maximum (360.0)
        while (vm.ThumbnailSize < MainViewModel.MaxThumbnailSize)
        {
            vm.ZoomInThumbnailCommand.Execute(null);
        }
        Assert.Equal(MainViewModel.MaxThumbnailSize, vm.ThumbnailSize);
        Assert.False(vm.CanZoomInThumbnail);
        Assert.False(vm.ZoomInThumbnailCommand.CanExecute(null));

        // Ensure does not exceed maximum
        vm.ZoomInThumbnailCommand.Execute(null);
        Assert.Equal(MainViewModel.MaxThumbnailSize, vm.ThumbnailSize);

        // Act: Zoom out to minimum (140.0)
        while (vm.ThumbnailSize > MainViewModel.MinThumbnailSize)
        {
            vm.ZoomOutThumbnailCommand.Execute(null);
        }
        Assert.Equal(MainViewModel.MinThumbnailSize, vm.ThumbnailSize);
        Assert.False(vm.CanZoomOutThumbnail);
        Assert.False(vm.ZoomOutThumbnailCommand.CanExecute(null));

        // Ensure does not fall below minimum
        vm.ZoomOutThumbnailCommand.Execute(null);
        Assert.Equal(MainViewModel.MinThumbnailSize, vm.ThumbnailSize);

        // Act: Zoom in from minimum re-enables zoom out
        vm.ZoomInThumbnailCommand.Execute(null);
        Assert.Equal(160.0, vm.ThumbnailSize);
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

        // 初期状態（通常ペン）の確認
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(Colors.Black, vm.SelectedColor);
        Assert.Equal(2.0, vm.StrokeThickness);

        // Act: 蛍光ペンに変更（TwoWayバインディング経由のプロパティ直接変更）
        vm.SelectedTool = EditorToolMode.Highlighter;

        // Assert: 色が黄色、太さが12pxに自動調整される
        Assert.Equal(EditorToolMode.Highlighter, vm.SelectedTool);
        Assert.Equal(Colors.Yellow, vm.SelectedColor);
        Assert.Equal(12.0, vm.StrokeThickness);

        // Act: 再び通常ペンに変更
        vm.SelectedTool = EditorToolMode.Pen;

        // Assert: 色が黒色、太さが2pxに復帰する
        Assert.Equal(EditorToolMode.Pen, vm.SelectedTool);
        Assert.Equal(Colors.Black, vm.SelectedColor);
        Assert.Equal(2.0, vm.StrokeThickness);

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

        // Act: エディタを閉じる
        vm.ClosePageDetailCommand.Execute(null);

        // Assert: 「PDF編集」タブ（0）へ自動復帰
        Assert.Equal(0, vm.SelectedRibbonTabIndex);
    }

    [Fact]
    public void DetailEditorViewModel_PresetAndCustomThickness_WorksCorrectly()
    {
        // Arrange
        var page = new PdfPageModel();
        var vm = new DetailEditorViewModel(
            page,
            new PDFBinder.Core.Services.PdfiumRenderer(),
            () => { },
            _ => null);

        // 初期値の確認
        Assert.False(vm.IsCustomThicknessOpen);

        // Act: 自由選択スライダーのトグル展開
        vm.ToggleCustomThicknessCommand.Execute(null);
        Assert.True(vm.IsCustomThicknessOpen);

        // Act: プリセット選択（0.5px）で太さ反映＆展開パネル自動クローズ
        vm.SetPresetThicknessCommand.Execute(0.5);
        Assert.Equal(0.5, vm.StrokeThickness);
        Assert.False(vm.IsCustomThicknessOpen);

        // Act: 文字列引数（"4.0"）でのプリセット選択
        vm.SetPresetThicknessCommand.Execute("4.0");
        Assert.Equal(4.0, vm.StrokeThickness);
        Assert.False(vm.IsCustomThicknessOpen);
    }
}
