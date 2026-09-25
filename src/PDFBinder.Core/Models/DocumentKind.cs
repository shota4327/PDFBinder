namespace PDFBinder.Core.Models;

/// <summary>
/// ドキュメントの種類を表す列挙体
/// </summary>
public enum DocumentKind
{
    /// <summary>PDFドキュメント</summary>
    Pdf,

    /// <summary>単一画像ファイル（JPEG / PNG）</summary>
    Image
}
