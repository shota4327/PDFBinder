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
}
