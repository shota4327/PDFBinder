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
        PageRotation rotation);

    /// <summary>
    /// 白紙ページのプレビュービットマップを生成します。
    /// </summary>
    BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation);
}
