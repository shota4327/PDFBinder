using System.Windows;
using PDFBinder.App.Controls;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// テキスト抽出、文字座標変換、リンク抽出、およびテキスト選択ロジックの単体テスト
/// </summary>
public class InteractiveDataExtractionTests
{
    [Fact]
    public void TransformCharacterBox_Rotate0_CalculatesCorrectWpfCoordinates()
    {
        // 1000x2000 の元画像ピクセルにおいて (100, 200, 300, 400)
        // 表示寸法 500x1000 の場合、比率は 0.5
        var rect = PdfiumRenderer.TransformCharacterBox(
            100, 200, 300, 400,
            1000, 2000,
            500, 1000,
            PageRotation.Rotate0);

        Assert.Equal(50.0, rect.X, 3);
        Assert.Equal(100.0, rect.Y, 3);
        Assert.Equal(100.0, rect.Width, 3);
        Assert.Equal(100.0, rect.Height, 3);
    }

    [Fact]
    public void TransformCharacterBox_Rotate90_AppliesRotationCorrectly()
    {
        // 表示寸法 800x600 (元画像 1000x1000)
        // 元 (100, 100, 200, 200) -> 90度回転
        var rect = PdfiumRenderer.TransformCharacterBox(
            100, 100, 200, 200,
            1000, 1000,
            800, 600,
            PageRotation.Rotate90);

        // Rotate90: new Rect(displayHeight - (y + h), x, h, w)
        // x = 0.1 * 800 = 80, y = 0.1 * 600 = 60, w = 80, h = 60
        // expected: X = 600 - 120 = 480, Y = 80, W = 60, H = 80
        Assert.Equal(480.0, rect.X, 3);
        Assert.Equal(80.0, rect.Y, 3);
        Assert.Equal(60.0, rect.Width, 3);
        Assert.Equal(80.0, rect.Height, 3);
    }

    [Fact]
    public void TransformPdfPointRect_Rotate0_InvertsYAxisCorrectly()
    {
        // PDF座標系は左下が (0,0)
        // ページ寸法 600pt x 800pt
        // PDF矩形: x=100, y=100, w=200, h=100 (上端は y=200)
        // WPF表示寸法: 600 x 800
        var rect = PdfiumRenderer.TransformPdfPointRect(
            100, 100, 200, 100,
            600, 800,
            600, 800,
            PageRotation.Rotate0);

        // WPFでは左上が原点なので、y_wpf = 800 - (100 + 100) = 600
        Assert.Equal(100.0, rect.X, 3);
        Assert.Equal(600.0, rect.Y, 3);
        Assert.Equal(200.0, rect.Width, 3);
        Assert.Equal(100.0, rect.Height, 3);
    }

    [Fact]
    public void PageInteractiveData_GetTextInRect_SelectsIntersectingCharacters()
    {
        // "HELLO" という5文字を配置
        var chars = new List<PdfTextCharacter>
        {
            new('H', new Rect(10, 20, 10, 20), 0),
            new('E', new Rect(20, 20, 10, 20), 1),
            new('L', new Rect(30, 20, 10, 20), 2),
            new('L', new Rect(40, 20, 10, 20), 3),
            new('O', new Rect(50, 20, 10, 20), 4),
        };

        var data = new PageInteractiveData(chars, Array.Empty<PdfLinkAnnotation>());
        Assert.Equal("HELLO", data.FullText);

        // "ELL" (x=20〜45) を囲む選択矩形
        var selection = new Rect(20, 10, 26, 40);
        var (text, selectedChars) = data.GetTextInRect(selection);

        Assert.Equal("ELL", text);
        Assert.Equal(3, selectedChars.Count);
        Assert.Equal('E', selectedChars[0].Character);
        Assert.Equal('L', selectedChars[1].Character);
        Assert.Equal('L', selectedChars[2].Character);
    }

    [Fact]
    public void PageInteractiveData_GetTextInRect_ReturnsEmpty_WhenNoIntersection()
    {
        var chars = new List<PdfTextCharacter>
        {
            new('A', new Rect(10, 20, 10, 20), 0),
        };

        var data = new PageInteractiveData(chars, Array.Empty<PdfLinkAnnotation>());
        var (text, selectedChars) = data.GetTextInRect(new Rect(100, 100, 50, 50));

        Assert.Empty(text);
        Assert.Empty(selectedChars);
    }

    [Fact]
    public void DetailPageItemViewModel_SelectedText_UpdatesHasSelectedText()
    {
        var page = new PdfPageModel { Width = 600, Height = 800, PageNumber = 1 };
        var vm = new DetailPageItemViewModel(page);

        Assert.False(vm.HasSelectedText);

        vm.SelectedText = "選択テキスト";
        Assert.True(vm.HasSelectedText);

        vm.SelectedText = "";
        Assert.False(vm.HasSelectedText);
    }

    [Fact]
    public void DetailPageItemViewModel_PageJumpRequested_FiresCorrectly()
    {
        var page = new PdfPageModel { Width = 600, Height = 800, PageNumber = 1 };
        var vm = new DetailPageItemViewModel(page);

        int firedPageIndex = -1;
        vm.PageJumpRequested += (target) => firedPageIndex = target;

        vm.RequestPageJump(5);
        Assert.Equal(5, firedPageIndex);
    }

    [Fact]
    public void EditorToolMode_TextSelect_CanBeSelectedInDetailEditor()
    {
        var page = new PdfPageModel { Width = 600, Height = 800, PageNumber = 1 };
        var doc = new PdfDocumentModel();
        doc.Pages.Add(page);

        var editorVm = new DetailEditorViewModel(
            page,
            new FakePdfRenderer(),
            () => { },
            (idx) => doc.Pages.ElementAtOrDefault(idx));

        editorVm.SelectedTool = EditorToolMode.TextSelect;
        Assert.Equal(EditorToolMode.TextSelect, editorVm.SelectedTool);
        Assert.False(editorVm.CanChangeColor);
        Assert.False(editorVm.CanChangeThickness);
    }

    private class FakePdfRenderer : IPdfRenderer
    {
        public Task<System.Windows.Media.Imaging.BitmapSource?> RenderPageAsync(
            string? filePath, int pageIndex, int targetWidth, int targetHeight,
            PageRotation rotation, CancellationToken cancellationToken = default)
            => Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);

        public System.Windows.Media.Imaging.BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
            => throw new NotImplementedException();

        public System.Windows.Media.Imaging.BitmapSource CompositeStrokes(
            System.Windows.Media.Imaging.BitmapSource baseImage,
            System.Windows.Ink.StrokeCollection strokes,
            double originalPageWidth, double originalPageHeight)
            => baseImage;

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(
            string? filePath, int pageIndex, double displayWidth, double displayHeight,
            PageRotation rotation, CancellationToken cancellationToken = default)
            => Task.FromResult(PageInteractiveData.Empty);
    }
}
