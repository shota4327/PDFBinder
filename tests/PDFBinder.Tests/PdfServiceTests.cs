using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using PdfSharp.Pdf;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="PdfService"/> の単体テストクラス
/// </summary>
public class PdfServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PdfService _service;

    public PdfServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"PDFBinder_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _service = new PdfService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    /// <summary>
    /// テスト用のPDFファイルを生成するヘルパーメソッド
    /// </summary>
    private string CreateSamplePdf(string fileName, int pageCount = 3)
    {
        string filePath = Path.Combine(_testDirectory, fileName);
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = doc.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(500);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(700);
        }
        doc.Save(filePath);
        return filePath;
    }

    [Fact]
    public async Task LoadDocumentAsync_ValidFile_ReturnsDocumentModelWithPages()
    {
        // Arrange
        string samplePdf = CreateSamplePdf("sample.pdf", 3);

        // Act
        var docModel = await _service.LoadDocumentAsync(samplePdf);

        // Assert
        Assert.NotNull(docModel);
        Assert.Equal(3, docModel.PageCount);
        Assert.Equal(1, docModel.Pages[0].PageNumber);
        Assert.Equal(2, docModel.Pages[1].PageNumber);
        Assert.Equal(3, docModel.Pages[2].PageNumber);
        Assert.Equal(500, docModel.Pages[0].Width);
        Assert.Equal(700, docModel.Pages[0].Height);
        Assert.False(docModel.IsModified);
    }

    [Fact]
    public void CreateBlankPage_Default_ReturnsA4BlankPageModel()
    {
        // Act
        var blank = _service.CreateBlankPage();

        // Assert
        Assert.NotNull(blank);
        Assert.True(blank.IsBlankPage);
        Assert.Null(blank.SourceFilePath);
        Assert.Equal(595.28, blank.Width);
        Assert.Equal(841.89, blank.Height);
        Assert.Equal(PageRotation.Rotate0, blank.Rotation);
    }

    [Fact]
    public async Task AppendDocumentAsync_AppendsPagesToTargetDocument()
    {
        // Arrange
        string pdf1 = CreateSamplePdf("doc1.pdf", 2);
        string pdf2 = CreateSamplePdf("doc2.pdf", 3);
        var targetDoc = await _service.LoadDocumentAsync(pdf1);

        // Act
        await _service.AppendDocumentAsync(targetDoc, pdf2);

        // Assert
        Assert.Equal(5, targetDoc.PageCount);
        Assert.Equal(5, targetDoc.Pages[4].PageNumber);
    }

    [Fact]
    public async Task SaveDocumentAsync_WithRotationAndInk_SavesSuccessfully()
    {
        // Arrange
        string sourcePdf = CreateSamplePdf("source.pdf", 2);
        var doc = await _service.LoadDocumentAsync(sourcePdf);

        // ページ1を時計回りに回転
        doc.Pages[0].RotateClockwise();

        // ページ2に手書きストロークを追加
        var points = new StylusPointCollection
        {
            new StylusPoint(10, 10),
            new StylusPoint(50, 50),
            new StylusPoint(100, 100)
        };
        var stroke = new Stroke(points);
        stroke.DrawingAttributes.Color = Colors.Red;
        stroke.DrawingAttributes.Width = 3;
        doc.Pages[1].InkStrokes.Add(stroke);

        // 白紙ページを追加
        var blank = _service.CreateBlankPage();
        doc.AddPage(blank);

        string outputPdf = Path.Combine(_testDirectory, "output.pdf");

        // Act
        await _service.SaveDocumentAsync(doc, outputPdf);

        // Assert
        Assert.True(File.Exists(outputPdf));
        var reloaded = await _service.LoadDocumentAsync(outputPdf);
        Assert.Equal(3, reloaded.PageCount);
        Assert.Equal(PageRotation.Rotate90, reloaded.Pages[0].Rotation);
    }

    [Fact]
    public async Task ExportPagesAsync_SelectedPagesOnly_ExportsCorrectSubset()
    {
        // Arrange
        string sourcePdf = CreateSamplePdf("source.pdf", 5);
        var doc = await _service.LoadDocumentAsync(sourcePdf);

        var selectedPages = new[] { doc.Pages[1], doc.Pages[3] };
        string exportedPdf = Path.Combine(_testDirectory, "extracted.pdf");

        // Act
        await _service.ExportPagesAsync(selectedPages, exportedPdf);

        // Assert
        Assert.True(File.Exists(exportedPdf));
        var reloaded = await _service.LoadDocumentAsync(exportedPdf);
        Assert.Equal(2, reloaded.PageCount);
    }

    [Fact]
    public async Task SplitAllPagesAsync_SplitsIntoIndividualPdfFiles()
    {
        // Arrange
        string sourcePdf = CreateSamplePdf("split_me.pdf", 3);
        var doc = await _service.LoadDocumentAsync(sourcePdf);
        string splitDir = Path.Combine(_testDirectory, "split_output");

        // Act
        int count = await _service.SplitAllPagesAsync(doc, splitDir, "doc");

        // Assert
        Assert.Equal(3, count);
        Assert.True(File.Exists(Path.Combine(splitDir, "doc_page_001.pdf")));
        Assert.True(File.Exists(Path.Combine(splitDir, "doc_page_002.pdf")));
        Assert.True(File.Exists(Path.Combine(splitDir, "doc_page_003.pdf")));
    }

    [Fact]
    public async Task SaveDocumentAsync_AfterPageRemoval_ReindexesOriginalPageIndex()
    {
        // Arrange: 5ページのPDFを作成して読み込み
        string sourcePdf = CreateSamplePdf("reindex_test.pdf", 5);
        var doc = await _service.LoadDocumentAsync(sourcePdf);

        // ページ1とページ2（0始まりのインデックス0と1）を削除
        doc.RemovePage(doc.Pages[0]);
        doc.RemovePage(doc.Pages[0]);
        Assert.Equal(3, doc.PageCount);

        // Act: 上書き保存を実行
        await _service.SaveDocumentAsync(doc, sourcePdf);

        // Assert: 保存後、残存ページのSourceFilePathとOriginalPageIndexが0, 1, 2に同期されていること
        for (int i = 0; i < doc.Pages.Count; i++)
        {
            Assert.Equal(sourcePdf, doc.Pages[i].SourceFilePath);
            Assert.Equal(i, doc.Pages[i].OriginalPageIndex);
        }

        // PDFiumレンダラーで各ページが正常にレンダリング可能であることを検証
        var renderer = new PdfiumRenderer();
        for (int i = 0; i < doc.Pages.Count; i++)
        {
            var bitmap = await renderer.RenderPageAsync(
                doc.Pages[i].SourceFilePath,
                doc.Pages[i].OriginalPageIndex,
                200,
                300,
                doc.Pages[i].RenderRotation);
            Assert.NotNull(bitmap);
        }
    }
}
