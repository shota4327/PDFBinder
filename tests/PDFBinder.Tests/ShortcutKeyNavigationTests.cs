using System.Threading;
using System.Windows;
using System.Windows.Input;
using PDFBinder.App;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// ページ移動ショートカット（PgUp, PgDn, 矢印キー↑↓←→）およびフォーカス非依存キー処理の単体テスト
/// </summary>
public class ShortcutKeyNavigationTests
{
    private static MainViewModel CreateTestViewModel(int pageCount = 3)
    {
        var vm = new MainViewModel();
        var doc = new PdfDocumentModel { FilePath = "test.pdf" };
        for (int i = 0; i < pageCount; i++)
        {
            doc.Pages.Add(new PdfPageModel
            {
                PageNumber = i + 1,
                OriginalPageIndex = i,
                Width = 595,
                Height = 842
            });
        }
        vm.Document = doc;
        vm.IsDetailViewActive = true;
        return vm;
    }

    [Theory]
    [InlineData(Key.Down)]
    [InlineData(Key.Right)]
    [InlineData(Key.PageDown)]
    public void HandlePageNavigation_NextPageKeys_NavigatesToNextPage(Key key)
    {
        // Arrange
        var vm = CreateTestViewModel(3);
        Assert.Equal(1, vm.CurrentPageNumber);
        Assert.True(vm.CanGoToNextPage);

        // Act
        bool handled = MainWindow.HandlePageNavigation(vm, key, ModifierKeys.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(2, vm.CurrentPageNumber);
    }

    [Theory]
    [InlineData(Key.Up)]
    [InlineData(Key.Left)]
    [InlineData(Key.PageUp)]
    public void HandlePageNavigation_PreviousPageKeys_NavigatesToPreviousPage(Key key)
    {
        // Arrange
        var vm = CreateTestViewModel(3);
        vm.CurrentPageNumber = 2;
        Assert.True(vm.CanGoToPreviousPage);

        // Act
        bool handled = MainWindow.HandlePageNavigation(vm, key, ModifierKeys.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(1, vm.CurrentPageNumber);
    }

    [Theory]
    [InlineData(Key.Up, ModifierKeys.Control)]
    [InlineData(Key.Down, ModifierKeys.Shift)]
    [InlineData(Key.Left, ModifierKeys.Alt)]
    [InlineData(Key.Right, ModifierKeys.Windows)]
    public void HandlePageNavigation_WithModifiers_ReturnsFalseAndDoesNotNavigate(Key key, ModifierKeys modifiers)
    {
        // Arrange
        var vm = CreateTestViewModel(3);
        vm.CurrentPageNumber = 2;

        // Act
        bool handled = MainWindow.HandlePageNavigation(vm, key, modifiers);

        // Assert
        Assert.False(handled);
        Assert.Equal(2, vm.CurrentPageNumber);
    }

    [Fact]
    public void HandlePageNavigation_WhenInGridView_ReturnsFalseAndDoesNotNavigate()
    {
        // Arrange
        var vm = CreateTestViewModel(3);
        vm.IsDetailViewActive = false; // 一覧グリッドビュー時

        // Act
        bool handledUp = MainWindow.HandlePageNavigation(vm, Key.Up, ModifierKeys.None);
        bool handledDown = MainWindow.HandlePageNavigation(vm, Key.Down, ModifierKeys.None);

        // Assert
        Assert.False(handledUp);
        Assert.False(handledDown);
    }

    [Fact]
    public void MainWindow_InputBindings_ContainArrowKeysAndPageUpDown()
    {
        var thread = new Thread(() =>
        {
            if (Application.Current == null)
            {
                var app = new PDFBinder.App.App();
                app.InitializeComponent();
            }

            var window = new MainWindow();

            var bindings = window.InputBindings.OfType<KeyBinding>().ToList();

            Assert.Contains(bindings, b => b.Key == Key.PageUp && b.Modifiers == ModifierKeys.None);
            Assert.Contains(bindings, b => b.Key == Key.PageDown && b.Modifiers == ModifierKeys.None);
            Assert.Contains(bindings, b => b.Key == Key.Up && b.Modifiers == ModifierKeys.None);
            Assert.Contains(bindings, b => b.Key == Key.Left && b.Modifiers == ModifierKeys.None);
            Assert.Contains(bindings, b => b.Key == Key.Down && b.Modifiers == ModifierKeys.None);
            Assert.Contains(bindings, b => b.Key == Key.Right && b.Modifiers == ModifierKeys.None);

            window.Close();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(TimeSpan.FromSeconds(10));
        Assert.True(finished, "テストがタイムアウトしました。");
    }
}
