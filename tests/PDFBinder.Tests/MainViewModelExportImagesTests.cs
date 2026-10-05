using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// MainViewModel における画像書き出し機能の単体テスト
/// </summary>
public class MainViewModelExportImagesTests
{
    [Fact]
    public void CanExportImages_ReflectsPageCountAndDialogVisibility()
    {
        var vm = new MainViewModel();
        Assert.False(vm.CanExportImages);

        vm.Document.AddPage(new PdfPageModel { Width = 595, Height = 842 });
        Assert.True(vm.CanExportImages);

        vm.ShowExportImagesDialog();
        Assert.True(vm.IsExportImagesDialogVisible);
        Assert.NotNull(vm.ExportImagesViewModel);
        Assert.False(vm.CanExportImages);

        vm.CloseExportImagesDialog();
        Assert.False(vm.IsExportImagesDialogVisible);
        Assert.Null(vm.ExportImagesViewModel);
        Assert.True(vm.CanExportImages);
    }

    [Fact]
    public void ShowExportImagesDialog_InitializesDialogCorrectly()
    {
        var fakeExportService = new FakeImageExportService();
        var vm = new MainViewModel(imageExportService: fakeExportService);
        vm.Document.AddPage(new PdfPageModel { Width = 595, Height = 842 });
        vm.Document.AddPage(new PdfPageModel { Width = 595, Height = 842 });

        vm.ShowExportImagesDialog();

        Assert.True(vm.IsExportImagesDialogVisible);
        Assert.NotNull(vm.ExportImagesViewModel);
        Assert.Equal(2, vm.ExportImagesViewModel.TotalExportPages);
        Assert.Equal(0, vm.ExportImagesViewModel.CurrentExportPageIndex);

        // クローズ
        vm.CloseExportImagesDialog();
        Assert.False(vm.IsExportImagesDialogVisible);
        Assert.Null(vm.ExportImagesViewModel);
    }

    [Fact]
    public void ExportImagesDialog_OnSuccess_UpdatesStatusMessage()
    {
        var fakeExportService = new FakeImageExportService();
        var vm = new MainViewModel(imageExportService: fakeExportService);
        vm.Document.AddPage(new PdfPageModel { Width = 595, Height = 842 });

        vm.ShowExportImagesDialog();
        Assert.NotNull(vm.ExportImagesViewModel);

        // ダイアログ内のダミーファイル選択
        vm.ExportImagesViewModel.PickSaveFileFunc = (name, filter, title) => @"C:\Export\output.png";

        // 書き出し実行
        vm.ExportImagesViewModel.ExecuteExportCommand.Execute(null);

        // 完了後にダイアログが閉じ、ステータスバーメッセージが更新されることを検証
        Assert.False(vm.IsExportImagesDialogVisible);
        Assert.Null(vm.ExportImagesViewModel);
        Assert.Contains("1 件の画像を書き出しました", vm.StatusMessage);
        Assert.Contains(@"C:\Export\output.png", vm.StatusMessage);
    }
}
