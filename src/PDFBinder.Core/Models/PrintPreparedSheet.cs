using System.Windows;
using System.Windows.Media.Imaging;

namespace PDFBinder.Core.Models;

/// <summary>
/// 印刷用にレンダリング済みの1ページ分画像と配置矩形情報
/// </summary>
public class PrintPreparedPlacement
{
    /// <summary>
    /// レンダリング済みのページビットマップ画像（凍結済み・スレッドセーフ）
    /// </summary>
    public BitmapSource? Image { get; }

    /// <summary>
    /// 用紙内での正規化された配置矩形（X, Y, Width, Height 各 0.0〜1.0）
    /// </summary>
    public Rect NormalizedBounds { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PrintPreparedPlacement(BitmapSource? image, Rect normalizedBounds)
    {
        Image = image;
        NormalizedBounds = normalizedBounds;
    }
}

/// <summary>
/// 印刷用に事前レンダリングされた1枚の用紙（シート）情報
/// </summary>
public class PrintPreparedSheet
{
    /// <summary>
    /// この用紙に配置されるレンダリング済みページのリスト
    /// </summary>
    public IReadOnlyList<PrintPreparedPlacement> Placements { get; }

    /// <summary>
    /// 用紙の向き
    /// </summary>
    public PrintOrientation Orientation { get; }

    /// <summary>
    /// 用紙サイズ
    /// </summary>
    public PrintPaperSize PaperSize { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PrintPreparedSheet(
        IReadOnlyList<PrintPreparedPlacement> placements,
        PrintOrientation orientation,
        PrintPaperSize paperSize)
    {
        Placements = placements;
        Orientation = orientation;
        PaperSize = paperSize;
    }
}
