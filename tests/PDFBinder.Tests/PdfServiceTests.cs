using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.Core.Exceptions;
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

    /// <summary>
    /// パスワード付きのテスト用PDFを生成します。
    /// </summary>
    private string CreateProtectedSamplePdf(string fileName, string password, int pageCount = 2)
    {
        string filePath = Path.Combine(_testDirectory, fileName);
        using var doc = new PdfDocument();
        doc.SecuritySettings.UserPassword = password;
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
    public async Task LoadDocumentAsync_ProtectedPdfWithoutPassword_ThrowsPdfPasswordRequiredException()
    {
        // Arrange
        string protectedPdf = CreateProtectedSamplePdf("locked.pdf", "testpass");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<PdfPasswordRequiredException>(
            () => _service.LoadDocumentAsync(protectedPdf));
        Assert.False(ex.IsInvalidPassword);
        Assert.Equal(protectedPdf, ex.FilePath);
    }

    [Fact]
    public async Task LoadDocumentAsync_ProtectedPdfWithWrongPassword_ThrowsPdfPasswordRequiredExceptionWithIsInvalidPassword()
    {
        // Arrange
        string protectedPdf = CreateProtectedSamplePdf("locked_wrong.pdf", "testpass");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<PdfPasswordRequiredException>(
            () => _service.LoadDocumentAsync(protectedPdf, "wrongpassword"));
        Assert.True(ex.IsInvalidPassword);
        Assert.Equal(protectedPdf, ex.FilePath);
    }

    [Fact]
    public async Task LoadDocumentAsync_ProtectedPdfWithCorrectPassword_LoadsSuccessfully()
    {
        // Arrange
        string protectedPdf = CreateProtectedSamplePdf("locked_correct.pdf", "mypassword", 3);

        // Act
        var doc = await _service.LoadDocumentAsync(protectedPdf, "mypassword");

        // Assert
        Assert.Equal(3, doc.PageCount);
        Assert.Equal("mypassword", doc.DocumentPassword);
        Assert.All(doc.Pages, p => Assert.Equal("mypassword", p.SourcePassword));
    }

    [Fact]
    public async Task SaveDocumentAsync_ProtectedPdf_PreservesPasswordEncryption()
    {
        // Arrange
        string protectedPdf = CreateProtectedSamplePdf("locked_save.pdf", "secret123", 2);
        var doc = await _service.LoadDocumentAsync(protectedPdf, "secret123");
        doc.Pages[0].Rotation = PageRotation.Rotate90;

        string outputPath = Path.Combine(_testDirectory, "saved_output.pdf");

        // Act
        await _service.SaveDocumentAsync(doc, outputPath);

        // Assert: 保存後、パスワードなしでの読み込みは例外となり、正しいパスワードで開けること
        await Assert.ThrowsAsync<PdfPasswordRequiredException>(
            () => _service.LoadDocumentAsync(outputPath));

        var savedDoc = await _service.LoadDocumentAsync(outputPath, "secret123");
        Assert.Equal(2, savedDoc.PageCount);
        Assert.Equal(PageRotation.Rotate90, savedDoc.Pages[0].Rotation);
    }

    [Fact]
    public async Task AppendDocumentAsync_ProtectedPdf_AppendsSuccessfullyWithPassword()
    {
        // Arrange
        string basePdf = CreateSamplePdf("base.pdf", 2);
        string protectedPdf = CreateProtectedSamplePdf("append_locked.pdf", "appendpass", 1);
        var doc = await _service.LoadDocumentAsync(basePdf);

        // Act
        await _service.AppendDocumentAsync(doc, protectedPdf, -1, "appendpass");

        // Assert
        Assert.Equal(3, doc.PageCount);
        Assert.Null(doc.Pages[0].SourcePassword);
        Assert.Equal("appendpass", doc.Pages[2].SourcePassword);
    }

    [Fact]
    public async Task ExportPagesAsync_ProtectedPdf_PreservesPasswordEncryption()
    {
        // Arrange
        string protectedPdf = CreateProtectedSamplePdf("export_locked.pdf", "exportpass", 2);
        var doc = await _service.LoadDocumentAsync(protectedPdf, "exportpass");

        string exportPath = Path.Combine(_testDirectory, "exported.pdf");

        // Act
        await _service.ExportPagesAsync(new[] { doc.Pages[0] }, exportPath, doc.DocumentPassword);

        // Assert
        await Assert.ThrowsAsync<PdfPasswordRequiredException>(
            () => _service.LoadDocumentAsync(exportPath));

        var exportedDoc = await _service.LoadDocumentAsync(exportPath, "exportpass");
        Assert.Equal(1, exportedDoc.PageCount);
    }

    [Fact]
    public async Task LoadDocumentAsync_PdfWithCropBox_UsesCropBoxDimensionsInsteadOfMediaBox()
    {
        // Arrange
        string filePath = Path.Combine(_testDirectory, "cropbox_sample.pdf");
        using (var doc = new PdfDocument())
        {
            // 1ページ目: MediaBoxはA4横(840x590)、CropBoxは縦長(360x510)
            var page1 = doc.AddPage();
            page1.MediaBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XPoint(0, 0), new PdfSharp.Drawing.XPoint(840, 590));
            page1.CropBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XPoint(420, 40), new PdfSharp.Drawing.XPoint(780, 550));

            // 2ページ目: MediaBoxはA4横(840x590)、CropBoxは横長(720x510)
            var page2 = doc.AddPage();
            page2.MediaBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XPoint(0, 0), new PdfSharp.Drawing.XPoint(840, 590));
            page2.CropBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XPoint(60, 40), new PdfSharp.Drawing.XPoint(780, 550));

            // 3ページ目: CropBoxなし（MediaBoxのみ 500x700）
            var page3 = doc.AddPage();
            page3.Width = PdfSharp.Drawing.XUnit.FromPoint(500);
            page3.Height = PdfSharp.Drawing.XUnit.FromPoint(700);

            doc.Save(filePath);
        }

        // Act
        var docModel = await _service.LoadDocumentAsync(filePath);

        // Assert
        Assert.Equal(3, docModel.PageCount);
        // 1ページ目: CropBoxの寸法（360x510）が採用され、縦長と認識されること
        Assert.Equal(360, docModel.Pages[0].Width, 1);
        Assert.Equal(510, docModel.Pages[0].Height, 1);
        Assert.True(docModel.Pages[0].DisplayWidth < docModel.Pages[0].DisplayHeight);

        // 2ページ目: CropBoxの寸法（720x510）が採用され、横長と認識されること
        Assert.Equal(720, docModel.Pages[1].Width, 1);
        Assert.Equal(510, docModel.Pages[1].Height, 1);
        Assert.True(docModel.Pages[1].DisplayWidth > docModel.Pages[1].DisplayHeight);

        // 3ページ目: CropBox未定義時はMediaBoxの寸法（500x700）が採用されること
        Assert.Equal(500, docModel.Pages[2].Width, 1);
        Assert.Equal(700, docModel.Pages[2].Height, 1);
    }

    [Fact]
    public async Task LoadDocumentAsync_ActualShaverPdf_RecognizesFirstPageAsPortrait()
    {
        // プロジェクトルートにある実ファイル シェーバー.pdf が存在する場合のテスト
        string shaverPdf = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\シェーバー.pdf"));
        if (!File.Exists(shaverPdf))
        {
            shaverPdf = Path.GetFullPath("シェーバー.pdf");
        }
        if (File.Exists(shaverPdf))
        {
            var docModel = await _service.LoadDocumentAsync(shaverPdf);
            // 1ページ目は縦長 (約 362.8 x 515.9 pt)
            Assert.True(docModel.Pages[0].DisplayWidth < docModel.Pages[0].DisplayHeight);
            Assert.InRange(docModel.Pages[0].DisplayWidth, 360, 365);
            Assert.InRange(docModel.Pages[0].DisplayHeight, 515, 520);

            // 2ページ目は横長 (約 725.7 x 515.9 pt)
            Assert.True(docModel.Pages[1].DisplayWidth > docModel.Pages[1].DisplayHeight);
            Assert.InRange(docModel.Pages[1].DisplayWidth, 720, 730);

            // 19ページ目（最終ページ）も縦長
            Assert.True(docModel.Pages[18].DisplayWidth < docModel.Pages[18].DisplayHeight);
            Assert.InRange(docModel.Pages[18].DisplayWidth, 360, 365);
        }
    }
}
