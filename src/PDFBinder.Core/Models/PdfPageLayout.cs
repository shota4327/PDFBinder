namespace PDFBinder.Core.Models;

/// <summary>
/// PDFドキュメントカタログ（/PageLayout）で指定される初期ページ配置モード
/// </summary>
public enum PdfPageLayout
{
    /// <summary>未指定（アプリの標準設定を使用）</summary>
    Default,

    /// <summary>単一ページ表示（/SinglePage）</summary>
    SinglePage,

    /// <summary>1列の連続スクロール表示（/OneColumn）</summary>
    OneColumn,

    /// <summary>2列の連続見開き表示・奇数ページ左（/TwoColumnLeft）</summary>
    TwoColumnLeft,

    /// <summary>2列の連続見開き表示・奇数ページ右＝表紙単独（/TwoColumnRight）</summary>
    TwoColumnRight,

    /// <summary>見開き表示・奇数ページ左（/TwoPageLeft）</summary>
    TwoPageLeft,

    /// <summary>見開き表示・奇数ページ右＝表紙単独（/TwoPageRight）</summary>
    TwoPageRight
}
