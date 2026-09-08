using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// PDFファイルの操作・編集・保存を担当するサービスインターフェース
/// </summary>
public interface IPdfService
{
    /// <summary>
    /// PDFファイルをファイルロックなしで非同期に読み込み、ドキュメントモデルを生成します。
    /// </summary>
    Task<PdfDocumentModel> LoadDocumentAsync(string filePath);

    /// <summary>
    /// 外部のPDFファイルを読み込み、指定位置に結合・挿入します。
    /// </summary>
    Task AppendDocumentAsync(PdfDocumentModel targetDoc, string filePath, int insertIndex = -1);

    /// <summary>
    /// 指定されたサイズ（または標準A4）の白紙ページモデルを作成します。
    /// </summary>
    PdfPageModel CreateBlankPage(double width = 595.28, double height = 841.89);

    /// <summary>
    /// ドキュメントを指定された出力先に保存します（安全な一時ファイル置換を使用）。
    /// </summary>
    Task SaveDocumentAsync(PdfDocumentModel doc, string outputPath);

    /// <summary>
    /// 指定されたページ群のみを別PDFファイルとして抽出・保存（分割）します。
    /// </summary>
    Task ExportPagesAsync(IEnumerable<PdfPageModel> pages, string outputPath);

    /// <summary>
    /// ドキュメントの全ページを1ページずつの個別PDFファイルに一括分割して出力します。
    /// </summary>
    Task<int> SplitAllPagesAsync(PdfDocumentModel doc, string outputDirectory, string baseFileName);
}
