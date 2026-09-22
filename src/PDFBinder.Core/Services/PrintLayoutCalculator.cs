using System.Windows;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// 印刷時のページ範囲解析および面付け（Layout）計算を行うサービスクラス
/// </summary>
public static class PrintLayoutCalculator
{
    /// <summary>
    /// カスタムページ範囲文字列（例: "1-3, 5, 8"）を解析し、0始まりのページインデックスリストを取得します。
    /// </summary>
    public static bool TryParsePageRange(
        string? rangeText,
        int totalPages,
        out List<int> pageIndices,
        out string? errorMessage)
    {
        pageIndices = new List<int>();
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rangeText))
        {
            errorMessage = "ページ番号または範囲を入力してください。";
            return false;
        }

        if (totalPages <= 0)
        {
            errorMessage = "ドキュメントにページが存在しません。";
            return false;
        }

        string[] tokens = rangeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            errorMessage = "有効なページ番号を指定してください。";
            return false;
        }

        foreach (string token in tokens)
        {
            if (!ProcessRangeToken(token, totalPages, pageIndices, out errorMessage))
            {
                pageIndices.Clear();
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 1つの範囲トークン（単一数値またはハイフン範囲）を処理します。
    /// </summary>
    private static bool ProcessRangeToken(
        string token,
        int totalPages,
        List<int> pageIndices,
        out string? errorMessage)
    {
        errorMessage = null;
        int hyphenIndex = token.IndexOf('-');

        if (hyphenIndex >= 0)
        {
            return ProcessHyphenRange(token, hyphenIndex, totalPages, pageIndices, out errorMessage);
        }

        if (!int.TryParse(token, out int singlePage))
        {
            errorMessage = $"「{token}」は無効な数値です。";
            return false;
        }

        if (singlePage < 1 || singlePage > totalPages)
        {
            errorMessage = $"ページ番号 {singlePage} は有効範囲外です（1〜{totalPages}）。";
            return false;
        }

        pageIndices.Add(singlePage - 1);
        return true;
    }

    /// <summary>
    /// ハイフンで区切られたページ範囲トークン（例: "2-5"）を処理します。
    /// </summary>
    private static bool ProcessHyphenRange(
        string token,
        int hyphenIndex,
        int totalPages,
        List<int> pageIndices,
        out string? errorMessage)
    {
        errorMessage = null;
        string startStr = token[..hyphenIndex].Trim();
        string endStr = token[(hyphenIndex + 1)..].Trim();

        if (!int.TryParse(startStr, out int startPage) || !int.TryParse(endStr, out int endPage))
        {
            errorMessage = $"「{token}」の範囲指定が無効です。";
            return false;
        }

        if (startPage < 1 || startPage > totalPages || endPage < 1 || endPage > totalPages)
        {
            errorMessage = $"範囲 {token} に含まれるページ番号が有効範囲外です（1〜{totalPages}）。";
            return false;
        }

        if (startPage <= endPage)
        {
            for (int p = startPage; p <= endPage; p++)
            {
                pageIndices.Add(p - 1);
            }
        }
        else
        {
            for (int p = startPage; p >= endPage; p--)
            {
                pageIndices.Add(p - 1);
            }
        }

        return true;
    }

    /// <summary>
    /// 印刷設定とドキュメント情報から、印刷用紙（シート）の一覧と各用紙上の配置を計算します。
    /// </summary>
    public static List<PrintSheetLayout> CalculateSheets(
        PrintSettings settings,
        int totalPages,
        int currentPageIndex)
    {
        if (totalPages <= 0) return new List<PrintSheetLayout>();

        List<int> targetPages = ResolveTargetPages(settings, totalPages, currentPageIndex);
        if (targetPages.Count == 0) return new List<PrintSheetLayout>();

        return settings.LayoutMode switch
        {
            PrintLayoutMode.FitToPage => CalculateFitToPageSheets(settings, targetPages),
            PrintLayoutMode.NUp => CalculateNUpSheets(settings, targetPages),
            PrintLayoutMode.Booklet => CalculateBookletSheets(settings, targetPages),
            _ => CalculateFitToPageSheets(settings, targetPages)
        };
    }

    /// <summary>
    /// 印刷対象の0始まりページ番号一覧を解決します。
    /// </summary>
    private static List<int> ResolveTargetPages(PrintSettings settings, int totalPages, int currentPageIndex)
    {
        return settings.RangeType switch
        {
            PrintRangeType.AllPages => Enumerable.Range(0, totalPages).ToList(),
            PrintRangeType.CurrentPage => new List<int> { Math.Clamp(currentPageIndex, 0, totalPages - 1) },
            PrintRangeType.Custom => TryParsePageRange(settings.CustomRangeText, totalPages, out var pages, out _)
                ? pages
                : Enumerable.Range(0, totalPages).ToList(),
            _ => Enumerable.Range(0, totalPages).ToList()
        };
    }

    /// <summary>
    /// 用紙サイズに合わせる（1枚1ページ）のレイアウトを計算します。
    /// </summary>
    private static List<PrintSheetLayout> CalculateFitToPageSheets(PrintSettings settings, List<int> pages)
    {
        var result = new List<PrintSheetLayout>(pages.Count);
        for (int i = 0; i < pages.Count; i++)
        {
            var sheet = new PrintSheetLayout(i + 1, settings.Orientation, settings.PaperSize);
            sheet.Placements.Add(new PrintPagePlacement(pages[i], new Rect(0, 0, 1, 1)));
            result.Add(sheet);
        }
        return result;
    }

    /// <summary>
    /// N-up（複数ページ集約）のレイアウトを計算します。
    /// </summary>
    private static List<PrintSheetLayout> CalculateNUpSheets(PrintSettings settings, List<int> pages)
    {
        int perSheet = (int)settings.NUpCount;
        int totalSheets = (int)Math.Ceiling((double)pages.Count / perSheet);
        var result = new List<PrintSheetLayout>(totalSheets);

        (int cols, int rows) = GetNUpGridDimensions(settings.NUpCount, settings.Orientation);

        for (int sheetIdx = 0; sheetIdx < totalSheets; sheetIdx++)
        {
            var sheet = new PrintSheetLayout(sheetIdx + 1, settings.Orientation, settings.PaperSize);
            for (int slot = 0; slot < perSheet; slot++)
            {
                int pageIdxInList = sheetIdx * perSheet + slot;
                int? page = pageIdxInList < pages.Count ? pages[pageIdxInList] : null;

                int col = slot % cols;
                int row = slot / cols;
                double cellWidth = 1.0 / cols;
                double cellHeight = 1.0 / rows;
                var bounds = new Rect(col * cellWidth, row * cellHeight, cellWidth, cellHeight);

                sheet.Placements.Add(new PrintPagePlacement(page, bounds));
            }
            result.Add(sheet);
        }

        return result;
    }

    /// <summary>
    /// N-up集約時の列数・行数を決定します。
    /// </summary>
    private static (int cols, int rows) GetNUpGridDimensions(NUpPagesPerSheet count, PrintOrientation orientation)
    {
        return count switch
        {
            NUpPagesPerSheet.Two => orientation == PrintOrientation.Landscape ? (2, 1) : (1, 2),
            NUpPagesPerSheet.Four => (2, 2),
            NUpPagesPerSheet.Eight => orientation == PrintOrientation.Landscape ? (4, 2) : (2, 4),
            _ => (1, 2)
        };
    }

    /// <summary>
    /// 冊子形式（中綴じ製本面付け）のレイアウトを計算します。
    /// </summary>
    private static List<PrintSheetLayout> CalculateBookletSheets(PrintSettings settings, List<int> pages)
    {
        // 4の倍数に切り上げ（不足分は白紙 null）
        int remainder = pages.Count % 4;
        int paddedCount = remainder == 0 ? pages.Count : pages.Count + (4 - remainder);

        var paddedPages = new List<int?>(paddedCount);
        foreach (int page in pages)
        {
            paddedPages.Add(page);
        }
        while (paddedPages.Count < paddedCount)
        {
            paddedPages.Add(null);
        }

        int totalSheets = paddedCount / 2;
        var result = new List<PrintSheetLayout>(totalSheets);

        for (int s = 0; s < totalSheets; s++)
        {
            // 冊子は常に横向き（Landscape）
            var sheet = new PrintSheetLayout(s + 1, PrintOrientation.Landscape, settings.PaperSize);
            int? leftPage;
            int? rightPage;

            if (s % 2 == 0)
            {
                // 表面（外側）
                leftPage = paddedPages[paddedCount - s - 1];
                rightPage = paddedPages[s];
            }
            else
            {
                // 裏面（内側）
                leftPage = paddedPages[s];
                rightPage = paddedPages[paddedCount - s - 1];
            }

            sheet.Placements.Add(new PrintPagePlacement(leftPage, new Rect(0, 0, 0.5, 1)));
            sheet.Placements.Add(new PrintPagePlacement(rightPage, new Rect(0.5, 0, 0.5, 1)));
            result.Add(sheet);
        }

        return result;
    }
}
