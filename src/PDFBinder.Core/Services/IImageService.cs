using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// 画像ファイル（JPEG / PNG）の読み込み・保存・PDF変換を提供するサービスインターフェース
/// </summary>
public interface IImageService
{
    /// <summary>
    /// 指定されたファイルパスがサポート対象の画像ファイル（JPEG / PNG）かどうかを判定します。
    /// </summary>
    /// <param name="filePath">検査対象のファイルパス</param>
    /// <returns>サポート対象の画像ファイルの場合はtrue、それ以外はfalse</returns>
    bool IsSupportedImage(string? filePath);

    /// <summary>
    /// 画像ファイルを読み込み、単一ページの画像ドキュメントモデルを構築します。
    /// </summary>
    /// <param name="filePath">読み込む画像ファイルのパス</param>
    /// <returns>構築された画像ドキュメントモデル</returns>
    Task<PdfDocumentModel> LoadImageDocumentAsync(string filePath);

    /// <summary>
    /// 画像ドキュメントモデルを指定パスに画像形式（JPEGまたはPNG）として保存します。
    /// 回転および手書きストロークが元画像の解像度に合わせて合成されます。
    /// </summary>
    /// <param name="doc">対象の画像ドキュメントモデル</param>
    /// <param name="outputPath">保存先ファイルパス</param>
    Task SaveImageAsync(PdfDocumentModel doc, string outputPath);

    /// <summary>
    /// 画像ドキュメントモデルを指定パスにPDF形式として書き出します。
    /// </summary>
    /// <param name="doc">対象の画像ドキュメントモデル</param>
    /// <param name="outputPath">出力先PDFファイルパス</param>
    Task SaveImageAsPdfAsync(PdfDocumentModel doc, string outputPath);
}
