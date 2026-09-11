using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFの読み込み・構造編集・結合・分割・保存を提供するサービス実装
/// </summary>
public class PdfService : IPdfService
{
    /// <inheritdoc/>
    public async Task<PdfDocumentModel> LoadDocumentAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("指定されたPDFファイルが見つかりません。", filePath);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
        using var stream = new MemoryStream(fileBytes);
        using var pdfDoc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        var model = new PdfDocumentModel
        {
            FilePath = filePath,
            IsModified = false
        };

        for (int i = 0; i < pdfDoc.PageCount; i++)
        {
            var pdfPage = pdfDoc.Pages[i];
            var pageRotation = PageRotationExtensions.FromDegrees(pdfPage.Rotate);
            var pageModel = new PdfPageModel
            {
                SourceFilePath = filePath,
                OriginalPageIndex = i,
                Width = pdfPage.Width.Point,
                Height = pdfPage.Height.Point,
                OriginalRotation = pageRotation,
                Rotation = pageRotation
            };
            model.AddPage(pageModel);
        }

        model.IsModified = false;
        return model;
    }

    /// <inheritdoc/>
    public async Task AppendDocumentAsync(PdfDocumentModel targetDoc, string filePath, int insertIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(targetDoc);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var importedDoc = await LoadDocumentAsync(filePath);
        int targetIndex = (insertIndex < 0 || insertIndex > targetDoc.Pages.Count)
            ? targetDoc.Pages.Count
            : insertIndex;

        foreach (var page in importedDoc.Pages)
        {
            targetDoc.InsertPage(targetIndex++, page);
        }
    }

    /// <inheritdoc/>
    public PdfPageModel CreateBlankPage(double width = 595.28, double height = 841.89)
    {
        return new PdfPageModel
        {
            SourceFilePath = null,
            OriginalPageIndex = -1,
            Width = width,
            Height = height,
            Rotation = PageRotation.Rotate0
        };
    }

    /// <inheritdoc/>
    public async Task SaveDocumentAsync(PdfDocumentModel doc, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // 一時ファイルパスに安全に保存
        string tempPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(),
            $"{Guid.NewGuid()}.tmp");

        try
        {
            await Task.Run(() => BuildAndSavePdf(doc.Pages, tempPath));
            SafeReplaceFile(tempPath, outputPath);
            doc.FilePath = outputPath;
            doc.IsModified = false;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <inheritdoc/>
    public async Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var pageList = pages.ToList();
        if (pageList.Count == 0)
        {
            throw new InvalidOperationException("エクスポートするページが指定されていません。");
        }

        await Task.Run(() => BuildAndSavePdf(pageList, outputPath));
    }

    /// <inheritdoc/>
    public async Task<int> SplitAllPagesAsync(PdfDocumentModel doc, string outputDirectory, string baseFileName)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string cleanBaseName = Path.GetFileNameWithoutExtension(baseFileName);
        int count = 0;

        for (int i = 0; i < doc.Pages.Count; i++)
        {
            var singlePage = new[] { doc.Pages[i] };
            string outFileName = $"{cleanBaseName}_page_{i + 1:D3}.pdf";
            string outPath = Path.Combine(outputDirectory, outFileName);

            await ExportPagesAsync(singlePage, outPath);
            count++;
        }

        return count;
    }

    /// <summary>
    /// ページリストから新しいPDFドキュメントを構築し指定パスに書き出します。
    /// </summary>
    private void BuildAndSavePdf(IEnumerable<PdfPageModel> pages, string destinationPath)
    {
        using var outputDoc = new PdfDocument();
        var sourceCache = new Dictionary<string, PdfDocument>();

        try
        {
            foreach (var pageModel in pages)
            {
                AppendPageToDocument(outputDoc, pageModel, sourceCache);
            }
            outputDoc.Save(destinationPath);
        }
        finally
        {
            foreach (var doc in sourceCache.Values)
            {
                doc.Dispose();
            }
        }
    }

    /// <summary>
    /// 単一ページモデルを構築中のPDFドキュメントに追加し、回転および手書きを描画します。
    /// </summary>
    private void AppendPageToDocument(
        PdfDocument outputDoc,
        PdfPageModel pageModel,
        Dictionary<string, PdfDocument> sourceCache)
    {
        PdfPage destPage;

        if (pageModel.IsBlankPage)
        {
            destPage = outputDoc.AddPage();
            destPage.Width = XUnit.FromPoint(pageModel.Width);
            destPage.Height = XUnit.FromPoint(pageModel.Height);
        }
        else
        {
            string sourcePath = pageModel.SourceFilePath!;
            if (!sourceCache.TryGetValue(sourcePath, out var sourceDoc))
            {
                byte[] bytes = File.ReadAllBytes(sourcePath);
                var stream = new MemoryStream(bytes);
                sourceDoc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                sourceCache[sourcePath] = sourceDoc;
            }

            destPage = outputDoc.AddPage(sourceDoc.Pages[pageModel.OriginalPageIndex]);
        }

        destPage.Rotate = (int)pageModel.Rotation;
        DrawInkStrokesOnPage(destPage, pageModel.InkStrokes, pageModel.Rotation);
    }

    /// <summary>
    /// PDFページ上に手書きストロークを描画します。
    /// </summary>
    private void DrawInkStrokesOnPage(PdfPage page, StrokeCollection strokes, PageRotation rotation)
    {
        if (strokes == null || strokes.Count == 0)
        {
            return;
        }

        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        double pageWidth = page.Width.Point;
        double pageHeight = page.Height.Point;

        foreach (var stroke in strokes)
        {
            DrawSingleStroke(gfx, stroke, rotation, pageWidth, pageHeight);
        }
    }

    /// <summary>
    /// 1本のストロークをXGraphicsに描画します。
    /// </summary>
    private void DrawSingleStroke(
        XGraphics gfx,
        Stroke stroke,
        PageRotation rotation,
        double pageWidth,
        double pageHeight)
    {
        var points = stroke.StylusPoints;
        if (points.Count < 2)
        {
            return;
        }

        var attr = stroke.DrawingAttributes;
        var mediaColor = attr.Color;
        byte alpha = attr.IsHighlighter ? (byte)120 : mediaColor.A;

        var color = XColor.FromArgb(alpha, mediaColor.R, mediaColor.G, mediaColor.B);
        var pen = new XPen(color, attr.Width)
        {
            LineCap = XLineCap.Round,
            LineJoin = XLineJoin.Round
        };

        var xPoints = new XPoint[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            xPoints[i] = TransformDisplayToPagePoint(
                points[i].X,
                points[i].Y,
                rotation,
                pageWidth,
                pageHeight);
        }

        gfx.DrawLines(pen, xPoints);
    }

    /// <summary>
    /// 画面表示座標系（DisplayWidth × DisplayHeight）の点を、
    /// PDFページの未回転用紙座標系（Width × Height）へ逆回転変換します。
    /// </summary>
    private static XPoint TransformDisplayToPagePoint(
        double x,
        double y,
        PageRotation rotation,
        double pageWidth,
        double pageHeight)
    {
        return rotation switch
        {
            PageRotation.Rotate90 => new XPoint(y, pageHeight - x),
            PageRotation.Rotate180 => new XPoint(pageWidth - x, pageHeight - y),
            PageRotation.Rotate270 => new XPoint(pageWidth - y, x),
            _ => new XPoint(x, y)
        };
    }

    /// <summary>
    /// ファイルをアトミックかつ安全に置換します。
    /// </summary>
    private static void SafeReplaceFile(string sourceTempFile, string targetFile)
    {
        if (File.Exists(targetFile))
        {
            File.Replace(sourceTempFile, targetFile, null);
        }
        else
        {
            File.Move(sourceTempFile, targetFile);
        }
    }
}
