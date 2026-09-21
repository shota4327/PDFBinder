using System.IO;
using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.Core.Helpers;
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
            RestoreInkStrokesIfPresent(pdfPage, pageModel);
            model.AddPage(pageModel);
        }

        model.ResetModifiedState();
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
            doc.ResetModifiedState();
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
        AttachInkAnnotationToPage(outputDoc, destPage, pageModel.InkStrokes, pageModel.Rotation);
    }

    /// <summary>
    /// PDFページに手書き注釈を設定します。既存の自前注釈はクリーンアップし、最新ストロークが存在する場合は新設します。
    /// </summary>
    private static void AttachInkAnnotationToPage(
        PdfDocument outputDoc,
        PdfPage page,
        StrokeCollection? strokes,
        PageRotation rotation)
    {
        PdfBinderInkAnnotation.RemoveBinderInkAnnotations(page);

        if (strokes == null || strokes.Count == 0)
        {
            return;
        }

        double pageWidth = page.Width.Point;
        double pageHeight = page.Height.Point;

        var annot = new PdfBinderInkAnnotation(outputDoc);
        annot.Elements.SetRectangle("/Rect", new PdfRectangle(new XRect(0, 0, pageWidth, pageHeight)));
        annot.Elements.SetString(PdfBinderInkAnnotation.InkKey, PdfBinderInkAnnotation.SerializeStrokes(strokes));
        annot.Elements["/AP"] = PdfBinderInkAnnotation.CreateAppearanceStream(
            outputDoc, strokes, rotation, pageWidth, pageHeight);

        page.Annotations.Add(annot);
    }

    /// <summary>
    /// ページ内の PDF Binder 専用手書き注釈からストロークを復元します。
    /// </summary>
    private static void RestoreInkStrokesIfPresent(PdfPage pdfPage, PdfPageModel pageModel)
    {
        for (int i = 0; i < pdfPage.Annotations.Count; i++)
        {
            var annot = pdfPage.Annotations[i];
            if (annot != null && annot.Elements.ContainsKey(PdfBinderInkAnnotation.InkKey))
            {
                string? base64 = annot.Elements.GetString(PdfBinderInkAnnotation.InkKey);
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    pageModel.InkStrokes = PdfBinderInkAnnotation.DeserializeStrokes(base64);
                    break;
                }
            }
        }
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

    /// <inheritdoc/>
    public async Task<List<PdfPageModel>> SplitPagesHalfAsync(
        IEnumerable<PdfPageModel> pages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var pageList = pages.ToList();
        if (pageList.Count == 0)
        {
            return new List<PdfPageModel>();
        }

        return await Task.Run(() => SplitPagesInternal(pageList, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 各ページを半分に分割し、新しいページモデル群を生成します。
    /// </summary>
    private static List<PdfPageModel> SplitPagesInternal(
        List<PdfPageModel> pages,
        CancellationToken cancellationToken)
    {
        var resultPages = new List<PdfPageModel>(pages.Count * 2);
        string tempDir = Path.Combine(Path.GetTempPath(), "PDFBinder", "splits");
        Directory.CreateDirectory(tempDir);
        string tempSplitPdfPath = Path.Combine(tempDir, $"{Guid.NewGuid()}.pdf");

        var sourceCache = new Dictionary<string, PdfDocument>();
        PdfDocument? splitDoc = null;
        try
        {
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessSinglePageSplit(page, ref splitDoc, tempSplitPdfPath, sourceCache, resultPages);
            }

            if (splitDoc != null)
            {
                splitDoc.Save(tempSplitPdfPath);
            }
        }
        finally
        {
            splitDoc?.Dispose();
            foreach (var doc in sourceCache.Values)
            {
                doc.Dispose();
            }
        }

        for (int i = 0; i < resultPages.Count; i++)
        {
            resultPages[i].PageNumber = i + 1;
        }

        return resultPages;
    }

    /// <summary>
    /// 単一ページの分割処理（白紙または実PDF）を行い結果リストに追加します。
    /// </summary>
    private static void ProcessSinglePageSplit(
        PdfPageModel page,
        ref PdfDocument? splitDoc,
        string tempSplitPdfPath,
        Dictionary<string, PdfDocument> sourceCache,
        List<PdfPageModel> resultPages)
    {
        bool isLandscape = page.DisplayWidth >= page.DisplayHeight;
        double splitOffset = isLandscape ? page.DisplayWidth / 2 : page.DisplayHeight / 2;
        var (strokes1, strokes2) = StrokeSplitHelper.SplitStrokes(page.InkStrokes, isLandscape, splitOffset);

        if (page.IsBlankPage)
        {
            double blankW = isLandscape ? page.DisplayWidth / 2 : page.DisplayWidth;
            double blankH = isLandscape ? page.DisplayHeight : page.DisplayHeight / 2;
            resultPages.Add(CreateSplitBlankPage(blankW, blankH, strokes1));
            resultPages.Add(CreateSplitBlankPage(blankW, blankH, strokes2));
            return;
        }

        splitDoc ??= new PdfDocument();
        string sourcePath = page.SourceFilePath!;
        if (!sourceCache.TryGetValue(sourcePath, out var sourceDoc))
        {
            byte[] bytes = File.ReadAllBytes(sourcePath);
            sourceDoc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            sourceCache[sourcePath] = sourceDoc;
        }

        var srcPage = sourceDoc.Pages[page.OriginalPageIndex];
        var (box1, box2) = CalculateCropBoxes(srcPage.Width.Point, srcPage.Height.Point, page.Rotation, isLandscape);

        int pageIndex1 = splitDoc.PageCount;
        var p1 = splitDoc.AddPage(srcPage);
        p1.CropBox = box1;
        p1.MediaBox = box1;

        int pageIndex2 = pageIndex1 + 1;
        var p2 = splitDoc.AddPage(srcPage);
        p2.CropBox = box2;
        p2.MediaBox = box2;

        resultPages.Add(CreateSplitPdfPage(box1.Width, box1.Height, page.Rotation, page.OriginalRotation, tempSplitPdfPath, pageIndex1, strokes1));
        resultPages.Add(CreateSplitPdfPage(box2.Width, box2.Height, page.Rotation, page.OriginalRotation, tempSplitPdfPath, pageIndex2, strokes2));
    }

    /// <summary>
    /// 分割後の白紙ページモデルを作成します。
    /// </summary>
    private static PdfPageModel CreateSplitBlankPage(double width, double height, StrokeCollection strokes)
    {
        return new PdfPageModel
        {
            SourceFilePath = null,
            OriginalPageIndex = -1,
            Width = width,
            Height = height,
            Rotation = PageRotation.Rotate0,
            OriginalRotation = PageRotation.Rotate0,
            InkStrokes = strokes,
            IsModified = true,
            IsThumbnailDirty = true
        };
    }

    /// <summary>
    /// 分割後の実PDFページモデルを作成します。
    /// </summary>
    private static PdfPageModel CreateSplitPdfPage(
        double width,
        double height,
        PageRotation rotation,
        PageRotation originalRotation,
        string filePath,
        int pageIndex,
        StrokeCollection strokes)
    {
        return new PdfPageModel
        {
            SourceFilePath = filePath,
            OriginalPageIndex = pageIndex,
            Width = width,
            Height = height,
            Rotation = rotation,
            OriginalRotation = originalRotation,
            InkStrokes = strokes,
            IsModified = true,
            IsThumbnailDirty = true
        };
    }

    /// <summary>
    /// 画面の表示向きとページの回転角度に基づいて、切り出す CropBox 領域を算出します。
    /// </summary>
    private static (PdfRectangle Box1, PdfRectangle Box2) CalculateCropBoxes(
        double w,
        double h,
        PageRotation rotation,
        bool isLandscape)
    {
        if (isLandscape)
        {
            return rotation switch
            {
                PageRotation.Rotate0 => (new PdfRectangle(new XPoint(0, 0), new XPoint(w / 2, h)), new PdfRectangle(new XPoint(w / 2, 0), new XPoint(w, h))),
                PageRotation.Rotate90 => (new PdfRectangle(new XPoint(0, 0), new XPoint(w, h / 2)), new PdfRectangle(new XPoint(0, h / 2), new XPoint(w, h))),
                PageRotation.Rotate180 => (new PdfRectangle(new XPoint(w / 2, 0), new XPoint(w, h)), new PdfRectangle(new XPoint(0, 0), new XPoint(w / 2, h))),
                PageRotation.Rotate270 => (new PdfRectangle(new XPoint(0, h / 2), new XPoint(w, h)), new PdfRectangle(new XPoint(0, 0), new XPoint(w, h / 2))),
                _ => (new PdfRectangle(new XPoint(0, 0), new XPoint(w / 2, h)), new PdfRectangle(new XPoint(w / 2, 0), new XPoint(w, h)))
            };
        }

        return rotation switch
        {
            PageRotation.Rotate0 => (new PdfRectangle(new XPoint(0, h / 2), new XPoint(w, h)), new PdfRectangle(new XPoint(0, 0), new XPoint(w, h / 2))),
            PageRotation.Rotate90 => (new PdfRectangle(new XPoint(w / 2, 0), new XPoint(w, h)), new PdfRectangle(new XPoint(0, 0), new XPoint(w / 2, h))),
            PageRotation.Rotate180 => (new PdfRectangle(new XPoint(0, 0), new XPoint(w, h / 2)), new PdfRectangle(new XPoint(0, h / 2), new XPoint(w, h))),
            PageRotation.Rotate270 => (new PdfRectangle(new XPoint(0, 0), new XPoint(w / 2, h)), new PdfRectangle(new XPoint(w / 2, 0), new XPoint(w, h))),
            _ => (new PdfRectangle(new XPoint(0, h / 2), new XPoint(w, h)), new PdfRectangle(new XPoint(0, 0), new XPoint(w, h / 2)))
        };
    }
}
