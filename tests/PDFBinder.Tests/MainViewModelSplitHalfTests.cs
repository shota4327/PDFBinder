using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="MainViewModel.SplitPagesHalfCommand"/> の単体テストクラス（Issue #137）
/// </summary>
public class MainViewModelSplitHalfTests
{
    [Fact]
    public async Task SplitPagesHalfCommand_SplitsPagesAndUpdatesDocument()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel
        {
            Width = 842,
            Height = 595,
            Rotation = PageRotation.Rotate0
        };
        vm.Document.AddPage(page);
        Assert.Equal(1, vm.Document.PageCount);

        // Act
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, vm.Document.PageCount);
        Assert.Equal(421, vm.Document.Pages[0].Width);
        Assert.Equal(595, vm.Document.Pages[0].Height);
        Assert.Equal(1, vm.Document.Pages[0].PageNumber);

        Assert.Equal(421, vm.Document.Pages[1].Width);
        Assert.Equal(595, vm.Document.Pages[1].Height);
        Assert.Equal(2, vm.Document.Pages[1].PageNumber);
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public async Task SplitPagesHalfCommand_UndoAndRedo_WorksCorrectly()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel
        {
            Width = 842,
            Height = 595,
            Rotation = PageRotation.Rotate0
        };
        vm.Document.AddPage(page);

        // Act - Split
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.True(vm.CanUndo);

        // Act - Undo
        vm.UndoCommand.Execute(null);
        Assert.Equal(1, vm.Document.PageCount);
        Assert.Equal(842, vm.Document.Pages[0].Width);
        Assert.Equal(595, vm.Document.Pages[0].Height);
        Assert.True(vm.CanRedo);

        // Act - Redo
        vm.RedoCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.Equal(421, vm.Document.Pages[0].Width);
        Assert.Equal(421, vm.Document.Pages[1].Width);
    }

    [Fact]
    public async Task SplitPagesHalfCommand_EmptyDocument_DoesNothing()
    {
        // Arrange
        var vm = new MainViewModel();
        Assert.Equal(0, vm.Document.PageCount);

        // Act
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(0, vm.Document.PageCount);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public async Task SplitPagesHalfCommand_InDetailView_SynchronizesDetailEditor()
    {
        // Arrange
        var vm = new MainViewModel();
        var page = new PdfPageModel
        {
            Width = 842,
            Height = 595,
            Rotation = PageRotation.Rotate0
        };
        vm.Document.AddPage(page);
        vm.OpenPageDetail(page);
        Assert.True(vm.IsDetailViewActive);
        Assert.Single(vm.DetailEditor!.Pages);

        // Act - 分割実行
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);

        // Assert - 詳細エディタのページ一覧が2ページに更新されていること
        Assert.Equal(2, vm.DetailEditor.Pages.Count);
        Assert.Equal(421, vm.DetailEditor.Pages[0].Page.Width);
        Assert.Equal(421, vm.DetailEditor.Pages[1].Page.Width);

        // Act - Undo
        vm.UndoCommand.Execute(null);
        Assert.Single(vm.DetailEditor.Pages);
        Assert.Equal(842, vm.DetailEditor.Pages[0].Page.Width);

        // Act - Redo
        vm.RedoCommand.Execute(null);
        Assert.Equal(2, vm.DetailEditor.Pages.Count);
        Assert.Equal(421, vm.DetailEditor.Pages[0].Page.Width);
    }

    /// <summary>
    /// 未読み込み状態から白紙追加を行った際、1ページ時は削除不可、2ページ目以降で削除コマンドが即時有効化されることを検証します（Issue #179）。
    /// </summary>
    [Fact]
    public void AddBlankPageCommand_SuccessivelyAdded_UpdatesDeleteCommandCanExecute()
    {
        // Arrange
        var vm = new MainViewModel();
        Assert.Equal(0, vm.Document.PageCount);
        Assert.False(vm.DeleteSelectedPagesCommand.CanExecute(null));

        // Act 1: 1ページ目の白紙追加
        vm.AddBlankPageCommand.Execute(null);
        Assert.Equal(1, vm.Document.PageCount);
        Assert.False(vm.DeleteSelectedPagesCommand.CanExecute(null));

        // Act 2: 2ページ目の白紙追加
        vm.AddBlankPageCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null));

        // Act 3: 6ページまで順次追加
        for (int i = 0; i < 4; i++)
        {
            vm.AddBlankPageCommand.Execute(null);
        }
        Assert.Equal(6, vm.Document.PageCount);
        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null));
        Assert.True(vm.SplitAllPagesCommand.CanExecute(null));
        Assert.True(vm.SplitPagesHalfCommand.CanExecute(null));
    }

    /// <summary>
    /// 6ページのドキュメントをページ分割（分割後12ページ）した際、詳細ビュー・グリッドビューを問わず削除および分割コマンドが有効な状態を維持することを検証します（Issue #179）。
    /// </summary>
    [Theory]
    [InlineData(true)]  // 詳細ビュー
    [InlineData(false)] // グリッドビュー
    public async Task SplitPagesHalfCommand_FromSixPages_RetainsDeleteAndSplitCommandsEnabled(bool isDetailView)
    {
        // Arrange: 6ページの白紙ドキュメントを用意
        var vm = new MainViewModel();
        for (int i = 0; i < 6; i++)
        {
            vm.AddBlankPageCommand.Execute(null);
        }
        Assert.Equal(6, vm.Document.PageCount);
        vm.IsDetailViewActive = isDetailView;

        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null));
        Assert.True(vm.SplitAllPagesCommand.CanExecute(null));
        Assert.True(vm.SplitPagesHalfCommand.CanExecute(null));

        // Act: ページ分割実行（6ページ -> 12ページ）
        await vm.SplitPagesHalfCommand.ExecuteAsync(null);

        // Assert: 12ページに倍増し、各種コマンドがすべて有効であること
        Assert.Equal(12, vm.Document.PageCount);
        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null), "DeleteSelectedPagesCommand should remain enabled after splitting to 12 pages");
        Assert.True(vm.SplitAllPagesCommand.CanExecute(null), "SplitAllPagesCommand should remain enabled after splitting to 12 pages");
        Assert.True(vm.SplitPagesHalfCommand.CanExecute(null), "SplitPagesHalfCommand should remain enabled after splitting to 12 pages");
    }

    /// <summary>
    /// ページ削除によって残り1ページになった際、削除コマンドが即座に無効化され、Undo/Redoで正しく同期されることを検証します（Issue #179）。
    /// </summary>
    [Fact]
    public void DeleteSelectedPages_ReducesToOnePage_DisablesDeleteCommand_AndUndoRedoWorks()
    {
        // Arrange: 2ページのドキュメントを作成
        var vm = new MainViewModel();
        vm.AddBlankPageCommand.Execute(null);
        vm.AddBlankPageCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null));

        // Act: 1ページを選択して削除 -> 残り1ページ
        vm.Document.Pages[0].IsSelected = true;
        vm.DeleteSelectedPagesCommand.Execute(null);

        // Assert: 1ページになったため削除コマンドが無効化されること
        Assert.Equal(1, vm.Document.PageCount);
        Assert.False(vm.DeleteSelectedPagesCommand.CanExecute(null), "DeleteSelectedPagesCommand should be disabled when page count is 1");

        // Act: Undo -> 2ページに復元
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Document.PageCount);
        Assert.True(vm.DeleteSelectedPagesCommand.CanExecute(null), "DeleteSelectedPagesCommand should be re-enabled after Undo restores 2 pages");

        // Act: Redo -> 再び1ページ
        vm.RedoCommand.Execute(null);
        Assert.Equal(1, vm.Document.PageCount);
        Assert.False(vm.DeleteSelectedPagesCommand.CanExecute(null), "DeleteSelectedPagesCommand should be disabled after Redo deletes to 1 page");
    }
}
