using System.Threading;
using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// 筆圧トグル機能およびペン・直線モード連動の単体テスト（Issue #88）
/// </summary>
public class DetailEditorPenPressureTests
{
    private class FakePdfRenderer : IPdfRenderer
    {
        public Task<System.Windows.Media.Imaging.BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);
        }

        public System.Windows.Media.Imaging.BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
        {
            throw new NotImplementedException();
        }

        public System.Windows.Media.Imaging.BitmapSource CompositeStrokes(
            System.Windows.Media.Imaging.BitmapSource baseImage,
            StrokeCollection strokes,
            double originalPageWidth,
            double originalPageHeight)
        {
            return baseImage;
        }

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(
            string? filePath,
            int pageIndex,
            double displayWidth,
            double displayHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PageInteractiveData.Empty);
        }
    }

    private static DetailEditorViewModel CreateViewModel(EditorToolMode initialTool = EditorToolMode.Pen)
    {
        var page = new PdfPageModel { Width = 595, Height = 842, PageNumber = 1 };
        var vm = new DetailEditorViewModel(page, new FakePdfRenderer(), () => { }, _ => null)
        {
            SelectedTool = initialTool
        };
        return vm;
    }

    /// <summary>
    /// 筆圧設定の初期値がOFF（false）であることを検証します。
    /// </summary>
    [Fact]
    public void IsPenPressureEnabled_DefaultIsFalse()
    {
        // Arrange & Act
        using var vm = CreateViewModel();

        // Assert
        Assert.False(vm.IsPenPressureEnabled);
    }

    /// <summary>
    /// 直線モードがOFFのとき、ペンツールのみCanTogglePenPressureがtrueになることを検証します。
    /// </summary>
    [Theory]
    [InlineData(EditorToolMode.Pen, true)]
    [InlineData(EditorToolMode.Highlighter, false)]
    [InlineData(EditorToolMode.Select, false)]
    [InlineData(EditorToolMode.EraserStroke, false)]
    [InlineData(EditorToolMode.EraserPoint, false)]
    [InlineData(EditorToolMode.Hand, false)]
    public void CanTogglePenPressure_OnlyTrueForPenWhenStraightLineIsFalse(EditorToolMode tool, bool expectedCanToggle)
    {
        // Arrange
        using var vm = CreateViewModel();
        vm.IsStraightLine = false;

        // Act
        vm.SelectedTool = tool;

        // Assert
        Assert.Equal(expectedCanToggle, vm.CanTogglePenPressure);
    }

    /// <summary>
    /// ペンツール選択時でも、直線モードがONのときはCanTogglePenPressureがfalseになることを検証します。
    /// </summary>
    [Fact]
    public void CanTogglePenPressure_FalseWhenStraightLineIsTrue()
    {
        // Arrange
        using var vm = CreateViewModel(EditorToolMode.Pen);
        Assert.True(vm.CanTogglePenPressure);

        // Act: 直線モードをONにする
        vm.IsStraightLine = true;

        // Assert: 筆圧トグルが無効化されること
        Assert.False(vm.CanTogglePenPressure);

        // Act: 直線モードをOFFに戻す
        vm.IsStraightLine = false;

        // Assert: 筆圧トグルが再度有効化されること
        Assert.True(vm.CanTogglePenPressure);
    }

    /// <summary>
    /// ツールを切り替えても筆圧ON/OFF設定が保持されることを検証します。
    /// </summary>
    [Fact]
    public void SelectedToolChanged_PreservesIsPenPressureEnabled()
    {
        // Arrange: 筆圧をONにする
        using var vm = CreateViewModel(EditorToolMode.Pen);
        vm.IsPenPressureEnabled = true;
        Assert.True(vm.IsPenPressureEnabled);

        // Act: 消しゴムに切り替える
        vm.SelectedTool = EditorToolMode.EraserStroke;

        // Assert: 筆圧設定はONのまま保持されること
        Assert.True(vm.IsPenPressureEnabled);
        Assert.False(vm.CanTogglePenPressure);

        // Act: 再びペンに戻す
        vm.SelectedTool = EditorToolMode.Pen;

        // Assert: 筆圧設定がONのままで操作可能であること
        Assert.True(vm.IsPenPressureEnabled);
        Assert.True(vm.CanTogglePenPressure);
    }

    /// <summary>
    /// ツール切り替え時および直線モード切り替え時にCanTogglePenPressureのPropertyChangedイベントが発生することを検証します。
    /// </summary>
    [Fact]
    public void CanTogglePenPressure_RaisesPropertyChanged()
    {
        // Arrange
        using var vm = CreateViewModel(EditorToolMode.Pen);
        var changedProperties = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        // Act 1: 直線モードを切り替え
        vm.IsStraightLine = true;
        Assert.Contains(nameof(DetailEditorViewModel.CanTogglePenPressure), changedProperties);

        // Act 2: ツールを切り替え
        changedProperties.Clear();
        vm.SelectedTool = EditorToolMode.Highlighter;
        Assert.Contains(nameof(DetailEditorViewModel.CanTogglePenPressure), changedProperties);
    }

    /// <summary>
    /// EditorInkCanvasにおいて、筆圧OFF時はIgnorePressure=trueとなり、ON時はIgnorePressure=falseとなることを検証します。
    /// </summary>
    [Fact]
    public void EditorInkCanvas_IsPenPressureEnabled_ControlsIgnorePressure()
    {
        var thread = new Thread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = false
            };

            // 初期状態（筆圧OFF）: IgnorePressure は true（筆圧を無視して均一な太さ）
            Assert.False(canvas.IsPenPressureEnabled);
            Assert.False(canvas.IsPenPressureActive);
            Assert.True(canvas.DefaultDrawingAttributes.IgnorePressure);

            // 筆圧ON: IgnorePressure は false（筆圧感知が有効）
            canvas.IsPenPressureEnabled = true;
            Assert.True(canvas.IsPenPressureActive);
            Assert.False(canvas.DefaultDrawingAttributes.IgnorePressure);

            // 直線モードON: 筆圧設定がONであってもIgnorePressureはtrue（直線は筆圧無効）
            canvas.IsStraightLine = true;
            Assert.False(canvas.IsPenPressureActive);
            Assert.True(canvas.DefaultDrawingAttributes.IgnorePressure);

            // 直線モードOFF: 再び筆圧有効に戻る
            canvas.IsStraightLine = false;
            Assert.True(canvas.IsPenPressureActive);
            Assert.False(canvas.DefaultDrawingAttributes.IgnorePressure);

            // 蛍光ペンモード: 筆圧設定がONであってもIgnorePressureはtrue（蛍光ペンは常に均一）
            canvas.ToolMode = EditorToolMode.Highlighter;
            Assert.False(canvas.IsPenPressureActive);
            Assert.True(canvas.DefaultDrawingAttributes.IgnorePressure);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }
}
