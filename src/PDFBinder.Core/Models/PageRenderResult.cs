using System.Windows.Media.Imaging;

namespace PDFBinder.Core.Models;

/// <summary>
/// ページのビットマップ画像とインタラクティブデータ（文字・リンク注釈）の一括レンダリング結果を表すモデル
/// </summary>
public record PageRenderResult(
    BitmapSource? Bitmap,
    PageInteractiveData InteractiveData
);
