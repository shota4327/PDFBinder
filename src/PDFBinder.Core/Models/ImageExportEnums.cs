namespace PDFBinder.Core.Models;

/// <summary>
/// 画像書き出しの出力フォーマット
/// </summary>
public enum ImageExportFormat
{
    /// <summary>PNG形式（ロスレス・透過対応）</summary>
    Png,

    /// <summary>JPEG形式（高圧縮）</summary>
    Jpeg
}

/// <summary>
/// 画像書き出しの出力解像度（DPI）
/// </summary>
public enum ImageExportDpi
{
    /// <summary>200 DPI（標準・軽量）</summary>
    Dpi200 = 200,

    /// <summary>300 DPI（高精細・印刷品質）</summary>
    Dpi300 = 300,

    /// <summary>400 DPI（超高精細・デフォルト）</summary>
    Dpi400 = 400,

    /// <summary>600 DPI（最高品質）</summary>
    Dpi600 = 600
}

/// <summary>
/// 画像書き出しの対象ページ範囲種別
/// </summary>
public enum ImageExportRangeType
{
    /// <summary>すべてのページ</summary>
    AllPages,

    /// <summary>現在選択中のページ</summary>
    CurrentPage,

    /// <summary>指定したページ番号・範囲</summary>
    Custom
}
