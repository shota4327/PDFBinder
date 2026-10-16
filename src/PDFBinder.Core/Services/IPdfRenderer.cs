using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFページのサムネイルおよびプレビュー画像のレンダリングを担当するサービスインターフェース
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// 指定されたPDFページをレンダリングし、WPFで表示可能なBitmapSourceを返します。
    /// </summary>
    Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたPDFページを実行優先度を指定してレンダリングし、WPFで表示可能なBitmapSourceを返します。
    /// </summary>
    Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority)
        => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken);

    /// <summary>
    /// 指定されたパスワード付きPDFページをレンダリングし、WPFで表示可能なBitmapSourceを返します。
    /// </summary>
    Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        string? password)
        => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken);

    /// <summary>
    /// 指定されたパスワード付きPDFページを実行優先度を指定してレンダリングし、WPFで表示可能なBitmapSourceを返します。
    /// </summary>
    Task<BitmapSource?> RenderPageAsync(
        string? filePath,
        int pageIndex,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority,
        string? password)
        => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, priority);

    /// <summary>
    /// 白紙ページのプレビュービットマップを生成します。
    /// </summary>
    BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation);

    /// <summary>
    /// 指定された基本画像の上に手書きストロークを縮小合成したビットマップを生成します。
    /// </summary>
    BitmapSource CompositeStrokes(
        BitmapSource baseImage,
        System.Windows.Ink.StrokeCollection strokes,
        double originalPageWidth,
        double originalPageHeight);

    /// <summary>
    /// 指定されたPDFページのテキスト・文字座標およびリンク注釈データを抽出します。
    /// </summary>
    Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたPDFページのテキスト・文字座標およびリンク注釈データを優先度を指定して抽出します。
    /// </summary>
    Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority)
        => ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken);

    /// <summary>
    /// 指定されたパスワード付きPDFページのテキスト・文字座標およびリンク注釈データを抽出します。
    /// </summary>
    Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        string? password)
        => ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken);

    /// <summary>
    /// 指定されたパスワード付きPDFページのテキスト・文字座標およびリンク注釈データを優先度を指定して抽出します。
    /// </summary>
    Task<PageInteractiveData> ExtractInteractiveDataAsync(
        string? filePath,
        int pageIndex,
        double displayWidth,
        double displayHeight,
        PageRotation rotation,
        CancellationToken cancellationToken,
        RenderPriority priority,
        string? password)
        => ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, priority);

    /// <summary>
    /// 指定されたPDFページの画像レンダリングとインタラクティブデータ抽出を同一のPDFリーダーセッションで一括実行します。
    /// </summary>
    async Task<PageRenderResult> RenderPageWithInteractiveDataAsync(
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
        var bmp = await RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, priority, password);
        var data = await ExtractInteractiveDataAsync(filePath, pageIndex, displayWidth, displayHeight, rotation, cancellationToken, priority, password);
        return new PageRenderResult(bmp, data);
    }

    /// <summary>
    /// 同一PDFファイルの指定された複数ページを同一のリーダーセッションで連続レンダリングします。
    /// </summary>
    async Task<IReadOnlyDictionary<int, BitmapSource>> RenderPagesBatchAsync(
        string? filePath,
        IReadOnlyList<int> pageIndices,
        int targetWidth,
        int targetHeight,
        PageRotation rotation,
        CancellationToken cancellationToken = default,
        RenderPriority priority = RenderPriority.Low,
        string? password = null)
    {
        var dict = new Dictionary<int, BitmapSource>();
        if (pageIndices == null) return dict;
        foreach (int idx in pageIndices)
        {
            var bmp = await RenderPageAsync(filePath, idx, targetWidth, targetHeight, rotation, cancellationToken, priority, password);
            if (bmp != null) dict[idx] = bmp;
        }
        return dict;
    }

    /// <summary>
    /// キャッシュされたファイルバイト列を解放します。
    /// 特定のファイルパスを指定した場合はそのファイルのみ、null を指定した場合は全キャッシュを解放します。
    /// </summary>
    /// <param name="filePath">解放対象のファイルパス（null時は全解放）</param>
    void InvalidateCache(string? filePath = null) { }
}
