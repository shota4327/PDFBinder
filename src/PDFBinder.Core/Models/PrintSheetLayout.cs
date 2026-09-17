using System.Windows;

namespace PDFBinder.Core.Models;

/// <summary>
/// 印刷用紙上に配置される1ページ分の割付情報
/// </summary>
public class PrintPagePlacement
{
    /// <summary>
    /// ドキュメント内のページ番号（0始まり）。白紙ページの場合は null。
    /// </summary>
    public int? PageIndex { get; set; }

    /// <summary>
    /// 用紙内での正規化された配置矩形（X, Y, Width, Height 各 0.0〜1.0）
    /// </summary>
    public Rect NormalizedBounds { get; set; }

    /// <summary>
    /// 初期化コンストラクタ
    /// </summary>
    public PrintPagePlacement(int? pageIndex, Rect normalizedBounds)
    {
        PageIndex = pageIndex;
        NormalizedBounds = normalizedBounds;
    }
}

/// <summary>
/// 印刷用紙（1枚）のレイアウト情報
/// </summary>
public class PrintSheetLayout
{
    /// <summary>
    /// シート番号（1始まり）
    /// </summary>
    public int SheetNumber { get; set; }

    /// <summary>
    /// 用紙の向き
    /// </summary>
    public PrintOrientation Orientation { get; set; }

    /// <summary>
    /// 用紙サイズ
    /// </summary>
    public PrintPaperSize PaperSize { get; set; }

    /// <summary>
    /// この用紙に配置されるページのリスト
    /// </summary>
    public List<PrintPagePlacement> Placements { get; set; } = new();

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PrintSheetLayout(int sheetNumber, PrintOrientation orientation, PrintPaperSize paperSize)
    {
        SheetNumber = sheetNumber;
        Orientation = orientation;
        PaperSize = paperSize;
    }
}
