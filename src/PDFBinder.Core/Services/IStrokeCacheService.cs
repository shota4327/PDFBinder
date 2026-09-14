using System.Windows.Ink;
using System.Windows.Media.Imaging;

namespace PDFBinder.Core.Services;

/// <summary>
/// 手書きストロークコレクションを指定解像度の透過ビットマップ画像へレンダリング・キャッシュ化するサービスインターフェース
/// </summary>
public interface IStrokeCacheService
{
    /// <summary>
    /// 指定された手書きストロークコレクションを透過ビットマップ画像にレンダリングします。
    /// </summary>
    /// <param name="strokes">レンダリング対象のストロークコレクション</param>
    /// <param name="pageWidth">ページの論理幅（DIP）</param>
    /// <param name="pageHeight">ページの論理高さ（DIP）</param>
    /// <param name="pixelWidth">生成するビットマップ画像のピクセル幅</param>
    /// <param name="pixelHeight">生成するビットマップ画像のピクセル高さ</param>
    /// <returns>レンダリングされた透過ビットマップ（Frozen）。ストロークが存在しない場合はnull。</returns>
    BitmapSource? RenderStrokeCache(
        StrokeCollection? strokes,
        double pageWidth,
        double pageHeight,
        int pixelWidth,
        int pixelHeight);
}
