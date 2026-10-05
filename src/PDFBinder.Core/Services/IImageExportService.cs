using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFページの画像（PNG / JPEG）書き出しおよび解像度計算を提供するサービスインターフェース
/// </summary>
public interface IImageExportService
{
    /// <summary>
    /// PDFのポイント寸法（72 DPI基準）から指定DPIにおけるピクセル寸法を計算します。
    /// </summary>
    /// <param name="pointWidth">ポイント幅</param>
    /// <param name="pointHeight">ポイント高さ</param>
    /// <param name="dpi">解像度（DPI）</param>
    /// <returns>計算されたピクセル幅および高さ</returns>
    (int Width, int Height) CalculatePixelSize(double pointWidth, double pointHeight, int dpi);

    /// <summary>
    /// 指定されたビットマップを指定フォーマット・解像度・ファイルパスで保存します。
    /// </summary>
    /// <param name="bitmap">保存対象のビットマップ</param>
    /// <param name="filePath">保存先ファイルパス</param>
    /// <param name="format">出力画像フォーマット（PNG / JPEG）</param>
    /// <param name="dpi">出力DPI</param>
    /// <param name="quality">JPEG品質（1〜100、デフォルト90）</param>
    Task SaveImageAsync(BitmapSource bitmap, string filePath, ImageExportFormat format, int dpi, int quality = 90);
}
