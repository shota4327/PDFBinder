using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using PDFBinder.Core.Helpers;
using PDFBinder.Core.Models;
using PdfSharp.Drawing;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFium（Docnet.Core）を利用したPDFページレンダリングサービス実装
/// </summary>
public class PdfiumRenderer : IPdfRenderer
{
    private readonly PriorityAsyncLock _renderLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, byte[] Bytes)> _fileBytesCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, bool HasMarker)> _hasBinderInkMarkerCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastWriteTimeUtc, Dictionary<int, List<PdfRawLinkAnnotation>> PageLinks)> _rawLinksCache = new();

    /// <summary>
    /// 現在のレンダリング排他ロックインスタンスを取得します（単体テスト検証用）。
    /// </summary>
    internal PriorityAsyncLock RenderLock => _renderLock;

    /// <summary>
    /// キャッシュされているファイル数を取得します（単体テスト検証用）。
    /// </summary>
    internal int CachedFileCount => _fileBytesCache.Count;

    /// <summary>
    /// マーカー判定がキャッシュされているファイル数を取得します（単体テスト検証用）。
    /// </summary>
    internal int CachedMarkerCount => _hasBinderInkMarkerCache.Count;

    /// <summary>
    /// リンク情報がキャッシュされているファイル数を取得します（単体テスト検証用）。
    /// </summary>
    internal int CachedLinkCount => _rawLinksCache.Count;

    /// <inheritdoc/>
    public void InvalidateCache(string? filePath = null)
    {
        if (filePath == null)
        {
            _fileBytesCache.Clear();
            _hasBinderInkMarkerCache.Clear();
            _rawLinksCache.Clear();
        }
        else
        {
            _fileBytesCache.TryRemove(filePath, out _);
            _hasBinderInkMarkerCache.TryRemove(filePath, out _);
            _rawLinksCache.TryRemove(filePath, out _);
        }
    }

    /// <summary>
    /// レンダリング用ファイルキャッシュをクリアします（後方互換・単体テスト用）。
    /// </summary>
    internal void ClearCache() => InvalidateCache();

    /// <inheritdoc/>
    public void RegisterBinderInkMarker(string filePath, bool hasMarker, byte[]? cleanRenderBytes = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var fileInfo = new FileInfo(filePath);
        var lastWrite = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow;

        _hasBinderInkMarkerCache[filePath] = (lastWrite, hasMarker);

        if (hasMarker && cleanRenderBytes != null)
        {
            _fileBytesCache[filePath] = (lastWrite, cleanRenderBytes);
        }
    }

    /// <summary>
    /// PDFiumネイティブライブラリ（DocLib.Instance）をバックグラウンド等で事前に初期化（プリウォーム）します。
    /// </summary>
    public static void Prewarm()
    {
        try
        {
            _ = DocLib.Instance;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PdfiumRenderer] Prewarm failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Warmup() => Prewarm();

    /// <inheritdoc/>
    public Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default)
        => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, RenderPriority.Normal, null);

    /// <inheritdoc/>
    public Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        string? password)
        => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, RenderPriority.Normal, password);

    /// <inheritdoc/>
    public Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority)
        => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, priority, null);

    /// <inheritdoc/>
    public async Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority,
        string? password)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
        }

        if (IsSupportedImage(filePath))
        {
            return await Task.Run(() => RenderImagePage(filePath, targetWidth, targetHeight, rotation, cancellationToken), cancellationToken);
        }

        var releaser = await _renderLock.AcquireAsync(priority, cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // 自前手書き注釈マーカーの有無をストリーム走査で高速判定（外部大容量PDFの全メモリ展開・複製を防止）
                    bool hasBinderInk = HasBinderInkAnnotationMarker(filePath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    // Docnet の PageDimensions(dimOne, dimTwo) は dimOne <= dimTwo (短辺, 長辺) を厳格に要求するため正規化
                    int minDim = Math.Min(targetWidth, targetHeight);
                    int maxDim = Math.Max(targetWidth, targetHeight);
                    var dimensions = new PageDimensions(Math.Max(1, minDim), Math.Max(1, maxDim));

                    // マーカーが存在しない通常のPDFは直接ファイルパスを渡し、PDFiumネイティブのオンデマンド読み込みを活用（メモリ割当ゼロ）
                    using var docReader = string.IsNullOrEmpty(password)
                        ? (hasBinderInk
                            ? DocLib.Instance.GetDocReader(GetRenderBytes(filePath, cancellationToken), dimensions)
                            : DocLib.Instance.GetDocReader(filePath, dimensions))
                        : (hasBinderInk
                            ? DocLib.Instance.GetDocReader(GetRenderBytes(filePath, cancellationToken), password, dimensions)
                            : DocLib.Instance.GetDocReader(filePath, password, dimensions));
                    if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
                    {
                        return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    using var pageReader = docReader.GetPageReader(pageIndex);
                    int actualWidth = pageReader.GetPageWidth();
                    int actualHeight = pageReader.GetPageHeight();
                    // 引数なし GetImage() により、PDFium の既知クラッシュ原因である FormFillEnvironment の不要な生成・破棄ループを完全に回避
                    byte[] rawBytes = pageReader.GetImage();

                    cancellationToken.ThrowIfCancellationRequested();
                    var bitmap = BitmapSource.Create(
                        actualWidth,
                        actualHeight,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null,
                        rawBytes,
                        actualWidth * 4);

                    bitmap.Freeze();

                    if (rotation != PageRotation.Rotate0)
                    {
                        var rotated = new TransformedBitmap(bitmap, new RotateTransform((int)rotation));
                        rotated.Freeze();
                        return rotated;
                    }

                    return bitmap;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            releaser.Dispose();
        }
    }

    /// <inheritdoc/>
    public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
    {
        int w = Math.Max(1, targetWidth);
        int h = Math.Max(1, targetHeight);

        if (rotation is PageRotation.Rotate90 or PageRotation.Rotate270)
        {
            (w, h) = (h, w);
        }

        var writeable = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[w * h * 4];
        Array.Fill(pixels, (byte)255);

        writeable.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        writeable.Freeze();
        return writeable;
    }

    /// <inheritdoc/>
    public BitmapSource CompositeStrokes(
        BitmapSource baseImage,
        System.Windows.Ink.StrokeCollection strokes,
        double originalPageWidth,
        double originalPageHeight)
    {
        if (baseImage == null) throw new ArgumentNullException(nameof(baseImage));
        if (strokes == null || strokes.Count == 0 || originalPageWidth <= 0 || originalPageHeight <= 0)
        {
            return baseImage;
        }

        int thumbWidth = baseImage.PixelWidth;
        int thumbHeight = baseImage.PixelHeight;
        if (thumbWidth <= 0 || thumbHeight <= 0)
        {
            return baseImage;
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(baseImage, new Rect(0, 0, thumbWidth, thumbHeight));

            double scaleX = (double)thumbWidth / originalPageWidth;
            double scaleY = (double)thumbHeight / originalPageHeight;
            dc.PushTransform(new ScaleTransform(scaleX, scaleY));
            strokes.Draw(dc);
            dc.Pop();
        }

        var renderTarget = new RenderTargetBitmap(thumbWidth, thumbHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    /// <inheritdoc/>
    public Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default)
        => ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, RenderPriority.Normal, null);

    /// <inheritdoc/>
    public Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        string? password)
        => ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, RenderPriority.Normal, password);

    /// <inheritdoc/>
    public Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority)
        => ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, priority, null);

    /// <inheritdoc/>
    public async Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority,
        string? password)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || IsSupportedImage(filePath))
        {
            return PageInteractiveData.Empty;
        }

        var releaser = await _renderLock.AcquireAsync(priority, cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    bool hasBinderInk = HasBinderInkAnnotationMarker(filePath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[]? bytes = hasBinderInk ? GetRenderBytes(filePath, cancellationToken) : null;
                    cancellationToken.ThrowIfCancellationRequested();

                    var characters = ExtractCharacters(filePath, bytes, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, password);
                    var links = ExtractLinks(filePath, bytes, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, password);

                    return new PageInteractiveData(characters, links, rotation);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return PageInteractiveData.Empty;
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            releaser.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<PageRenderResult> RenderPageWithInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default,
        RenderPriority priority = RenderPriority.Normal,
        string? password = null,
        IReadOnlyList<PdfRawLinkAnnotation>? cachedRawLinks = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            var blankBmp = CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
            return new PageRenderResult(blankBmp, PageInteractiveData.Empty);
        }

        if (IsSupportedImage(filePath))
        {
            var imgBmp = await Task.Run(() => RenderImagePage(filePath, targetWidth, targetHeight, rotation, cancellationToken), cancellationToken);
            return new PageRenderResult(imgBmp, PageInteractiveData.Empty);
        }

        var releaser = await _renderLock.AcquireAsync(priority, cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    bool hasBinderInk = HasBinderInkAnnotationMarker(filePath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    int minDim = Math.Min(targetWidth, targetHeight);
                    int maxDim = Math.Max(targetWidth, targetHeight);
                    var dimensions = new PageDimensions(Math.Max(1, minDim), Math.Max(1, maxDim));

                    byte[]? renderBytes = hasBinderInk ? GetRenderBytes(filePath, cancellationToken) : null;
                    using var docReader = string.IsNullOrEmpty(password)
                        ? (renderBytes != null
                            ? DocLib.Instance.GetDocReader(renderBytes, dimensions)
                            : DocLib.Instance.GetDocReader(filePath, dimensions))
                        : (renderBytes != null
                            ? DocLib.Instance.GetDocReader(renderBytes, password, dimensions)
                            : DocLib.Instance.GetDocReader(filePath, password, dimensions));

                    if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
                    {
                        var blankBmp = CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
                        return new PageRenderResult(blankBmp, PageInteractiveData.Empty);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    using var pageReader = docReader.GetPageReader(pageIndex);
                    int actualWidth = pageReader.GetPageWidth();
                    int actualHeight = pageReader.GetPageHeight();
                    byte[] rawBytes = pageReader.GetImage();
                    var rawCharacters = pageReader.GetCharacters();

                    cancellationToken.ThrowIfCancellationRequested();
                    var bitmap = BitmapSource.Create(
                        actualWidth,
                        actualHeight,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null,
                        rawBytes,
                        actualWidth * 4);
                    bitmap.Freeze();

                    if (rotation != PageRotation.Rotate0)
                    {
                        var rotated = new TransformedBitmap(bitmap, new RotateTransform((int)rotation));
                        rotated.Freeze();
                        bitmap = rotated;
                    }

                    var characters = BuildCharacters(rawCharacters, actualWidth, actualHeight, displayWidth, displayHeight, rotation, cancellationToken);
                    var links = ExtractLinks(filePath, renderBytes, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, password, cachedRawLinks);

                    var interactiveData = new PageInteractiveData(characters, links, rotation);
                    return new PageRenderResult(bitmap, interactiveData);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    var blankBmp = CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
                    return new PageRenderResult(blankBmp, PageInteractiveData.Empty);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            releaser.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<int, BitmapSource>> RenderPagesBatchAsync(
        string? filePath,
        IReadOnlyList<int> pageIndices,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default,
        RenderPriority priority = RenderPriority.Low,
        string? password = null)
    {
        var result = new Dictionary<int, BitmapSource>();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || pageIndices == null || pageIndices.Count == 0)
        {
            return result;
        }

        if (IsSupportedImage(filePath))
        {
            var imgBmp = await Task.Run(() => RenderImagePage(filePath, targetWidth, targetHeight, rotation, cancellationToken), cancellationToken);
            if (imgBmp != null)
            {
                foreach (int idx in pageIndices) result[idx] = imgBmp;
            }
            return result;
        }

        var releaser = await _renderLock.AcquireAsync(priority, cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    bool hasBinderInk = HasBinderInkAnnotationMarker(filePath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    int minDim = Math.Min(targetWidth, targetHeight);
                    int maxDim = Math.Max(targetWidth, targetHeight);
                    var dimensions = new PageDimensions(Math.Max(1, minDim), Math.Max(1, maxDim));

                    byte[]? renderBytes = hasBinderInk ? GetRenderBytes(filePath, cancellationToken) : null;
                    using var docReader = string.IsNullOrEmpty(password)
                        ? (renderBytes != null
                            ? DocLib.Instance.GetDocReader(renderBytes, dimensions)
                            : DocLib.Instance.GetDocReader(filePath, dimensions))
                        : (renderBytes != null
                            ? DocLib.Instance.GetDocReader(renderBytes, password, dimensions)
                            : DocLib.Instance.GetDocReader(filePath, password, dimensions));

                    int totalPages = docReader.GetPageCount();
                    foreach (int idx in pageIndices)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (idx < 0 || idx >= totalPages)
                        {
                            result[idx] = CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
                            continue;
                        }

                        using var pageReader = docReader.GetPageReader(idx);
                        int actualWidth = pageReader.GetPageWidth();
                        int actualHeight = pageReader.GetPageHeight();
                        byte[] rawBytes = pageReader.GetImage();

                        var bmp = BitmapSource.Create(
                            actualWidth,
                            actualHeight,
                            96,
                            96,
                            PixelFormats.Bgra32,
                            null,
                            rawBytes,
                            actualWidth * 4);
                        bmp.Freeze();
                        if (rotation != PageRotation.Rotate0)
                        {
                            var rotated = new TransformedBitmap(bmp, new RotateTransform((int)rotation));
                            rotated.Freeze();
                            result[idx] = rotated;
                        }
                        else
                        {
                            result[idx] = bmp;
                        }
                    }

                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return result;
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            releaser.Dispose();
        }
    }

    /// <summary>
    /// Docnet.Core を用いて指定ページの文字および座標を抽出します。
    /// </summary>
    private List<PdfTextCharacter> ExtractCharacters(
        string filePath,
        byte[]? pdfBytes,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        string? password)
    {
        var dimensions = new PageDimensions(1080, 1920);

        using var docReader = string.IsNullOrEmpty(password)
            ? (pdfBytes != null
                ? DocLib.Instance.GetDocReader(pdfBytes, dimensions)
                : DocLib.Instance.GetDocReader(filePath, dimensions))
            : (pdfBytes != null
                ? DocLib.Instance.GetDocReader(pdfBytes, password, dimensions)
                : DocLib.Instance.GetDocReader(filePath, password, dimensions));
        if (pageIndex < 0 || pageIndex >= docReader.GetPageCount())
        {
            return new List<PdfTextCharacter>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var pageReader = docReader.GetPageReader(pageIndex);
        int rawWidth = pageReader.GetPageWidth();
        int rawHeight = pageReader.GetPageHeight();
        var rawCharacters = pageReader.GetCharacters();

        return BuildCharacters(rawCharacters, rawWidth, rawHeight, displayWidth, displayHeight, rotation, cancellationToken);
    }

    /// <summary>
    /// 文字リストを WPF 表示座標系に変換して構築します。
    /// </summary>
    private static List<PdfTextCharacter> BuildCharacters(
        IEnumerable<Character>? rawCharacters,
        int rawWidth,
        int rawHeight,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken)
    {
        var result = new List<PdfTextCharacter>();
        if (rawCharacters == null || rawWidth <= 0 || rawHeight <= 0) return result;

        int index = 0;
        foreach (var c in rawCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var box = TransformCharacterBox(
                c.Box.Left, c.Box.Top, c.Box.Right, c.Box.Bottom,
                rawWidth, rawHeight, displayWidth, displayHeight, rotation);

            result.Add(new PdfTextCharacter(c.Char, box, index++));
        }

        return result;
    }

    /// <summary>
    /// 指定ページのリンク注釈を表示座標系に変換して取得します（キャッシュ対応）。
    /// </summary>
    private List<PdfLinkAnnotation> ExtractLinks(
        string filePath,
        byte[]? pdfBytes,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        string? password,
        IReadOnlyList<PdfRawLinkAnnotation>? cachedRawLinks = null)
    {
        if (cachedRawLinks != null)
        {
            return PdfLinkHelper.TransformRawLinks(cachedRawLinks, displayWidth, displayHeight, rotation);
        }

        var allLinks = GetOrLoadRawLinks(filePath, pdfBytes, password, cancellationToken);
        if (allLinks.TryGetValue(pageIndex, out var pageRawLinks))
        {
            return PdfLinkHelper.TransformRawLinks(pageRawLinks, displayWidth, displayHeight, rotation);
        }

        return new List<PdfLinkAnnotation>();
    }

    /// <summary>
    /// PDFファイル全体の生リンク注釈情報を一括取得・解析します（キャッシュ対応）。
    /// </summary>
    private Dictionary<int, List<PdfRawLinkAnnotation>> GetOrLoadRawLinks(
        string filePath,
        byte[]? pdfBytes,
        string? password,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var lastWrite = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;

        if (_rawLinksCache.TryGetValue(filePath, out var cached) && cached.LastWriteTimeUtc == lastWrite)
        {
            return cached.PageLinks;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var pageLinks = new Dictionary<int, List<PdfRawLinkAnnotation>>();

        using var doc = string.IsNullOrEmpty(password)
            ? (pdfBytes != null
                ? PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdfBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import)
                : PdfSharp.Pdf.IO.PdfReader.Open(filePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            : (pdfBytes != null
                ? PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdfBytes), password, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import)
                : PdfSharp.Pdf.IO.PdfReader.Open(filePath, password, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import));

        for (int i = 0; i < doc.PageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var links = PdfLinkHelper.ExtractRawLinks(doc.Pages[i], doc, cancellationToken);
            if (links.Count > 0)
            {
                pageLinks[i] = links;
            }
        }

        _rawLinksCache[filePath] = (lastWrite, pageLinks);
        return pageLinks;
    }


    /// <summary>
    /// Docnet.Core のピクセル文字座標を WPF 表示座標系に変換します。
    /// </summary>
    public static Rect TransformCharacterBox(
        int left, int top, int right, int bottom,
        int rawWidth, int rawHeight,
        double displayWidth, double displayHeight,
        PageRotation rotation)
    {
        double normLeft = (double)Math.Min(left, right) / rawWidth;
        double normTop = (double)Math.Min(top, bottom) / rawHeight;
        double normRight = (double)Math.Max(left, right) / rawWidth;
        double normBottom = (double)Math.Max(top, bottom) / rawHeight;

        double x = normLeft * displayWidth;
        double y = normTop * displayHeight;
        double w = Math.Max(1.0, (normRight - normLeft) * displayWidth);
        double h = Math.Max(1.0, (normBottom - normTop) * displayHeight);

        return ApplyRotation(new Rect(x, y, w, h), displayWidth, displayHeight, rotation);
    }

    /// <summary>
    /// PdfSharp のポイント単位矩形（左下原点）を WPF 表示座標系に変換します。
    /// </summary>
    public static Rect TransformPdfPointRect(
        double pdfX, double pdfY, double pdfW, double pdfH,
        double ptWidth, double ptHeight,
        double displayWidth, double displayHeight,
        PageRotation rotation)
        => PdfLinkHelper.TransformPdfPointRect(pdfX, pdfY, pdfW, pdfH, ptWidth, ptHeight, displayWidth, displayHeight, rotation);

    /// <summary>
    /// 回転角度に応じた矩形のアフィン変換を適用します。
    /// </summary>
    private static Rect ApplyRotation(Rect rect, double displayWidth, double displayHeight, PageRotation rotation)
        => PdfLinkHelper.ApplyRotation(rect, displayWidth, displayHeight, rotation);


    /// <summary>
    /// レンダリング用に PDF バイト列から自前の手書き注釈を除外します（他社製注釈はそのまま保持）。
    /// </summary>
    private static bool IsSupportedImage(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
    }

    private static BitmapSource? RenderImagePage(
        string filePath,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = File.ReadAllBytes(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource? bitmap = BitmapTransformHelper.CreateDetachedBitmap(decoder.Frames[0]);
        if (bitmap == null)
        {
            return null;
        }

        if (rotation != PageRotation.Rotate0)
        {
            bitmap = BitmapTransformHelper.CreateRotatedBitmap(bitmap, (int)rotation) ?? bitmap;
        }

        if (targetWidth > 0 && targetHeight > 0 &&
            (bitmap.PixelWidth != targetWidth || bitmap.PixelHeight != targetHeight))
        {
            double sx = (double)targetWidth / bitmap.PixelWidth;
            double sy = (double)targetHeight / bitmap.PixelHeight;
            var scaled = new TransformedBitmap(bitmap, new ScaleTransform(sx, sy));
            scaled.Freeze();
            return scaled;
        }

        if (!bitmap.IsFrozen && bitmap.CanFreeze)
        {
            bitmap.Freeze();
        }
        return bitmap;
    }

    /// <summary>
    /// 指定されたファイルパスのPDFバイト列を取得し、自前手書き注釈がある場合のみ除去したバイト列を返却します（キャッシュ対応）。
    /// </summary>
    private byte[] GetRenderBytes(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var lastWrite = fileInfo.LastWriteTimeUtc;

        if (_fileBytesCache.TryGetValue(filePath, out var cached) && cached.LastWriteTimeUtc == lastWrite)
        {
            return cached.Bytes;
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] rawBytes = File.ReadAllBytes(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] processedBytes = RemoveBinderInkAnnotationsIfPresent(rawBytes);
        _fileBytesCache[filePath] = (lastWrite, processedBytes);
        return processedBytes;
    }

    /// <summary>
    /// PDFバイト列内に自前手書き注釈が存在する場合のみ、それらを除去したバイト列を生成して返却します。
    /// 自前手書き注釈が存在しない場合は、無駄な再シリアライズ（doc.Save）を行わず元のバイト列をそのまま返却します。
    /// </summary>
    private static byte[] RemoveBinderInkAnnotationsIfPresent(byte[] pdfBytes)
        => PdfBinderInkAnnotation.RemoveBinderInkAnnotationsIfPresent(pdfBytes);

    private static readonly byte[] BinderInkMarkerBytes = System.Text.Encoding.ASCII.GetBytes(PdfBinderInkAnnotation.InkKey);

    /// <summary>
    /// PDFファイル内に本アプリ専用の手書き注釈マーカー（/PdfBinderInk）が存在するかを、
    /// ファイル全体をメモリに展開することなくストリーム走査で高速判定します（キャッシュ対応）。
    /// </summary>
    private bool HasBinderInkAnnotationMarker(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists) return false;

        var lastWrite = fileInfo.LastWriteTimeUtc;
        if (_hasBinderInkMarkerCache.TryGetValue(filePath, out var cached) && cached.LastWriteTimeUtc == lastWrite)
        {
            return cached.HasMarker;
        }

        bool hasMarker = ScanStreamForMarker(filePath, BinderInkMarkerBytes, cancellationToken);
        _hasBinderInkMarkerCache[filePath] = (lastWrite, hasMarker);
        return hasMarker;
    }

    /// <summary>
    /// ファイルストリームを固定長バッファで走査し、指定されたマーカーバイト列が含まれるかを検索します。
    /// </summary>
    private static bool ScanStreamForMarker(string filePath, byte[] marker, CancellationToken cancellationToken)
    {
        const int bufferSize = 64 * 1024;
        byte[] buffer = new byte[bufferSize];
        int overlap = marker.Length - 1;
        int carryOver = 0;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesRead = fs.Read(buffer, carryOver, bufferSize - carryOver);
                int totalBytes = carryOver + bytesRead;
                if (totalBytes < marker.Length) break;

                if (ContainsSubsequence(buffer, totalBytes, marker))
                {
                    return true;
                }

                if (bytesRead == 0) break;

                Array.Copy(buffer, totalBytes - overlap, buffer, 0, overlap);
                carryOver = overlap;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 読み込み例外発生時は安全側に倒してマーカーあり（フル解析へフォールバック）として扱う
            return true;
        }

        return false;
    }

    /// <summary>
    /// バッファ内に指定されたバイト列が含まれているかを検索します。
    /// </summary>
    private static bool ContainsSubsequence(byte[] buffer, int length, byte[] pattern)
    {
        int limit = length - pattern.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }
}

