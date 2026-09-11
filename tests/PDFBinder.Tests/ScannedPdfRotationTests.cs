using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// スキャンPDFおよび回転メタデータ付きPDFの表示・レンダリング・手書き保存に関する単体テスト
/// </summary>
public class ScannedPdfRotationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _sampleScannedPdfPath;
    private readonly PdfService _pdfService;
    private readonly PdfiumRenderer _renderer;

    public ScannedPdfRotationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_ScannedTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);

        // Assets/sample_scanned.pdf のパスを解決
        string binDir = AppDomain.CurrentDomain.BaseDirectory;
        string assetPath = Path.Combine(binDir, "Assets", "sample_scanned.pdf");
        if (!File.Exists(assetPath))
        {
            // テスト実行ディレクトリからの相対探索フォールバック
            assetPath = Path.GetFullPath(Path.Combine(binDir, "..", "..", "..", "Assets", "sample_scanned.pdf"));
        }

        _sampleScannedPdfPath = assetPath;
        _pdfService = new PdfService();
        _renderer = new PdfiumRenderer();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task LoadDocumentAsync_ScannedPdf_PreservesOriginalRotationAndSetsDeltaToZero()
    {
        // Arrange & Act
        var doc = await _pdfService.LoadDocumentAsync(_sampleScannedPdfPath);

        // Assert
        Assert.NotNull(doc);
        Assert.True(doc.Pages.Count > 0);

        var page = doc.Pages[0];
        Assert.Equal(PageRotation.Rotate270, page.OriginalRotation);
        Assert.Equal(PageRotation.Rotate270, page.Rotation);
        Assert.Equal(PageRotation.Rotate0, page.RenderRotation);

        // 用紙自体は横向き（Width > Height）だが、Rotate270により表示上は縦向き（DisplayWidth < DisplayHeight）
        Assert.True(page.Width > page.Height);
        Assert.True(page.DisplayWidth < page.DisplayHeight);
    }

    [Fact]
    public async Task RenderThumbnail_ScannedPdf_RendersPortraitWithoutDoubleRotation()
    {
        // Arrange
        var doc = await _pdfService.LoadDocumentAsync(_sampleScannedPdfPath);
        var page = doc.Pages[0];

        // Act: サムネイル（360x504）を差分回転（RenderRotation = Rotate0）でレンダリング
        var thumb = await _renderer.RenderPageAsync(
            page.SourceFilePath,
            page.OriginalPageIndex,
            360,
            504,
            page.RenderRotation);

        // Assert
        Assert.NotNull(thumb);
        // 二重回転されず正立（縦向き: 高さ > 幅）で出力されること
        Assert.True(thumb.PixelHeight > thumb.PixelWidth, $"PixelHeight({thumb.PixelHeight}) should be > PixelWidth({thumb.PixelWidth})");
    }

    [Fact]
    public async Task RenderDetailView_ScannedPdf_SucceedsWithoutArgumentException()
    {
        // Arrange
        var doc = await _pdfService.LoadDocumentAsync(_sampleScannedPdfPath);
        var page = doc.Pages[0];

        int targetWidth = (int)(page.DisplayWidth * DetailEditorViewModel.EditorRenderScale);
        int targetHeight = (int)(page.DisplayHeight * DetailEditorViewModel.EditorRenderScale);

        // Act: 詳細ビューサイズでレンダリング
        var detail = await _renderer.RenderPageAsync(
            page.SourceFilePath,
            page.OriginalPageIndex,
            targetWidth,
            targetHeight,
            page.RenderRotation);

        // Assert: Docnet の例外（dimOne > dimTwo）が発生せず正しく正立高解像度ビットマップが生成されること
        Assert.NotNull(detail);
        Assert.True(detail.PixelHeight > detail.PixelWidth, $"Detail PixelHeight({detail.PixelHeight}) should be > PixelWidth({detail.PixelWidth})");
        Assert.True(detail.PixelWidth > 1000);
        Assert.True(detail.PixelHeight > 1000);
    }

    [Fact]
    public async Task UserRotateOperations_CorrectlyUpdatesRenderRotationDelta()
    {
        // Arrange
        var doc = await _pdfService.LoadDocumentAsync(_sampleScannedPdfPath);
        var page = doc.Pages[0];

        Assert.Equal(PageRotation.Rotate270, page.OriginalRotation);
        Assert.Equal(PageRotation.Rotate0, page.RenderRotation);

        // Act 1: ユーザーが右に90度回転
        page.RotateClockwise();

        // Assert 1: (270 + 90) % 360 = 0度
        Assert.Equal(PageRotation.Rotate0, page.Rotation);
        // 元PDF(270度)に対して90度回転した状態
        Assert.Equal(PageRotation.Rotate90, page.RenderRotation);
        // 表示上は横向き
        Assert.True(page.DisplayWidth > page.DisplayHeight);

        // Act 2: さらに右に90度回転（合計180度回転）
        page.RotateClockwise();

        // Assert 2
        Assert.Equal(PageRotation.Rotate90, page.Rotation);
        Assert.Equal(PageRotation.Rotate180, page.RenderRotation);

        // Act 3: 左に90度回転（戻す）
        page.RotateCounterClockwise();

        // Assert 3
        Assert.Equal(PageRotation.Rotate0, page.Rotation);
        Assert.Equal(PageRotation.Rotate90, page.RenderRotation);
    }

    [Fact]
    public async Task DetailEditorViewModel_ScannedPdf_LoadsBackgroundSuccessfully()
    {
        // Arrange
        var doc = await _pdfService.LoadDocumentAsync(_sampleScannedPdfPath);
        var page = doc.Pages[0];

        var vm = new DetailEditorViewModel(
            page,
            _renderer,
            () => { },
            i => i >= 0 && i < doc.Pages.Count ? doc.Pages[i] : null);

        // Act
        await vm.LoadPageBackgroundAsync();

        // Assert: 背景画像が白紙ではなく正立ビットマップとして生成されていること
        Assert.NotNull(vm.PageBackground);
        Assert.True(vm.PageBackground.PixelHeight > vm.PageBackground.PixelWidth);
    }

    [Fact]
    public async Task SaveDocumentAsync_ScannedPdfWithInk_TransformsCoordinatesAndSavesSuccessfully()
    {
        // Arrange
        var doc = await _pdfService.LoadDocumentAsync(_sampleScannedPdfPath);
        var page = doc.Pages[0];

        // 画面（表示座標系 595x841）の左上付近にストロークを追加
        var points = new StylusPointCollection
        {
            new StylusPoint(50, 50),
            new StylusPoint(100, 100)
        };
        var stroke = new Stroke(points);
        stroke.DrawingAttributes.Color = Colors.Blue;
        stroke.DrawingAttributes.Width = 2.0;
        page.InkStrokes.Add(stroke);

        string outputPath = Path.Combine(_testDirectory, "saved_scanned.pdf");

        // Act: 保存
        await _pdfService.SaveDocumentAsync(doc, outputPath);

        // Assert: ファイルが生成され、再読み込み可能
        Assert.True(File.Exists(outputPath));
        var reloaded = await _pdfService.LoadDocumentAsync(outputPath);
        Assert.Single(reloaded.Pages);
        Assert.Equal(PageRotation.Rotate270, reloaded.Pages[0].Rotation);
        Assert.Equal(PageRotation.Rotate270, reloaded.Pages[0].OriginalRotation);
    }
}
