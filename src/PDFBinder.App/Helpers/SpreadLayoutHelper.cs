#nullable enable
using PDFBinder.App.Models;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Helpers;

/// <summary>
/// 見開き表示における1行（最大2ページ）の配置定義を表します。
/// </summary>
/// <param name="RowIndex">0始まりの行インデックス</param>
/// <param name="PrimaryPageIndex">行内の代表ページ（若い番号側）の0始まりインデックス</param>
/// <param name="SecondaryPageIndex">行内の相方ページの0始まりインデックス（単独行時はnull）</param>
/// <param name="LeftPageIndex">画面左側に配置するページの0始まりインデックス（空欄時はnull）</param>
/// <param name="RightPageIndex">画面右側に配置するページの0始まりインデックス（空欄時はnull）</param>
public readonly record struct SpreadRowDefinition(
    int RowIndex,
    int PrimaryPageIndex,
    int? SecondaryPageIndex,
    int? LeftPageIndex,
    int? RightPageIndex);

/// <summary>
/// 見開き表示の行割り当て、ページインデックス変換、およびPDF初期表示フラグの解決を行うヘルパークラス。
/// </summary>
public static class SpreadLayoutHelper
{
    /// <summary>
    /// ページ数・表紙単独設定・綴じ方向から見開き行の定義一覧を構築します。
    /// </summary>
    public static IReadOnlyList<SpreadRowDefinition> BuildRows(
        int pageCount,
        bool showCoverPageSeparately,
        PdfReadingDirection direction)
    {
        if (pageCount <= 0)
        {
            return Array.Empty<SpreadRowDefinition>();
        }

        var rows = new List<SpreadRowDefinition>((pageCount + 1) / 2 + 1);
        int cursor = 0;

        if (showCoverPageSeparately)
        {
            rows.Add(CreateSinglePageRow(0, 0, direction, isCover: true));
            cursor = 1;
        }

        while (cursor < pageCount)
        {
            int rowIndex = rows.Count;
            if (cursor + 1 < pageCount)
            {
                rows.Add(CreatePairRow(rowIndex, cursor, cursor + 1, direction));
                cursor += 2;
            }
            else
            {
                rows.Add(CreateSinglePageRow(rowIndex, cursor, direction, isCover: false));
                cursor++;
            }
        }

        return rows;
    }

    /// <summary>
    /// 指定されたページインデックスが属する見開き行のインデックスを返します（範囲外は -1）。
    /// </summary>
    public static int GetRowIndexForPage(int pageIndex, int pageCount, bool showCoverPageSeparately)
    {
        if (pageIndex < 0 || pageIndex >= pageCount)
        {
            return -1;
        }

        if (showCoverPageSeparately)
        {
            return pageIndex == 0 ? 0 : 1 + (pageIndex - 1) / 2;
        }

        return pageIndex / 2;
    }

    /// <summary>
    /// 指定されたページインデックスから1つ前の見開き行の代表ページインデックスを返します（先頭行時は -1）。
    /// </summary>
    public static int GetPreviousSpreadPageIndex(int currentPageIndex, int pageCount, bool showCoverPageSeparately)
    {
        int rowIndex = GetRowIndexForPage(currentPageIndex, pageCount, showCoverPageSeparately);
        if (rowIndex <= 0)
        {
            return -1;
        }

        int prevRowIndex = rowIndex - 1;
        if (showCoverPageSeparately)
        {
            return prevRowIndex == 0 ? 0 : 1 + (prevRowIndex - 1) * 2;
        }

        return prevRowIndex * 2;
    }

    /// <summary>
    /// 指定されたページインデックスから1つ次の見開き行の代表ページインデックスを返します（最終行時は -1）。
    /// </summary>
    public static int GetNextSpreadPageIndex(int currentPageIndex, int pageCount, bool showCoverPageSeparately)
    {
        int rowIndex = GetRowIndexForPage(currentPageIndex, pageCount, showCoverPageSeparately);
        if (rowIndex < 0)
        {
            return -1;
        }

        int nextStartIndex = showCoverPageSeparately
            ? 1 + rowIndex * 2
            : (rowIndex + 1) * 2;

        return nextStartIndex < pageCount ? nextStartIndex : -1;
    }

    /// <summary>
    /// PDFメタデータ（/PageLayout および /ViewerPreferences /Direction）を詳細ビューの初期表示設定へ変換します。
    /// </summary>
    public static (DetailPageViewMode ViewMode, bool ShowCoverPageSeparately, PdfReadingDirection ReadingDirection) ResolveInitialDisplaySettings(
        PdfPageLayout pageLayout,
        PdfReadingDirection readingDirection)
    {
        return pageLayout switch
        {
            PdfPageLayout.OneColumn => (DetailPageViewMode.Continuous, true, readingDirection),
            PdfPageLayout.TwoPageLeft => (DetailPageViewMode.TwoPage, false, readingDirection),
            PdfPageLayout.TwoPageRight => (DetailPageViewMode.TwoPage, true, readingDirection),
            PdfPageLayout.TwoColumnLeft => (DetailPageViewMode.TwoPageContinuous, false, readingDirection),
            PdfPageLayout.TwoColumnRight => (DetailPageViewMode.TwoPageContinuous, true, readingDirection),
            _ => (DetailPageViewMode.SinglePage, true, readingDirection)
        };
    }

    private static SpreadRowDefinition CreateSinglePageRow(
        int rowIndex,
        int pageIndex,
        PdfReadingDirection direction,
        bool isCover)
    {
        bool placeOnRight = isCover
            ? direction == PdfReadingDirection.LeftToRight
            : direction == PdfReadingDirection.RightToLeft;

        return placeOnRight
            ? new SpreadRowDefinition(rowIndex, pageIndex, null, null, pageIndex)
            : new SpreadRowDefinition(rowIndex, pageIndex, null, pageIndex, null);
    }

    private static SpreadRowDefinition CreatePairRow(
        int rowIndex,
        int firstPageIndex,
        int secondPageIndex,
        PdfReadingDirection direction)
    {
        return direction == PdfReadingDirection.RightToLeft
            ? new SpreadRowDefinition(rowIndex, firstPageIndex, secondPageIndex, secondPageIndex, firstPageIndex)
            : new SpreadRowDefinition(rowIndex, firstPageIndex, secondPageIndex, firstPageIndex, secondPageIndex);
    }
}
