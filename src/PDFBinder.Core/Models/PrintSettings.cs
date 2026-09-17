using CommunityToolkit.Mvvm.ComponentModel;

namespace PDFBinder.Core.Models;

/// <summary>
/// 印刷設定を保持するモデルクラス
/// </summary>
public partial class PrintSettings : ObservableObject
{
    [ObservableProperty]
    private string _printerName = string.Empty;

    [ObservableProperty]
    private int _copies = 1;

    [ObservableProperty]
    private PrintOrientation _orientation = PrintOrientation.Portrait;

    [ObservableProperty]
    private PrintPaperSize _paperSize = PrintPaperSize.A4;

    [ObservableProperty]
    private PrintDuplexMode _duplexMode = PrintDuplexMode.OneSided;

    [ObservableProperty]
    private PrintRangeType _rangeType = PrintRangeType.AllPages;

    [ObservableProperty]
    private string _customRangeText = string.Empty;

    [ObservableProperty]
    private PrintLayoutMode _layoutMode = PrintLayoutMode.FitToPage;

    [ObservableProperty]
    private NUpPagesPerSheet _nUpCount = NUpPagesPerSheet.Two;

    /// <summary>
    /// 設定内容をデフォルト値にリセットします。
    /// </summary>
    public void ResetToDefault()
    {
        PrinterName = string.Empty;
        Copies = 1;
        Orientation = PrintOrientation.Portrait;
        PaperSize = PrintPaperSize.A4;
        DuplexMode = PrintDuplexMode.OneSided;
        RangeType = PrintRangeType.AllPages;
        CustomRangeText = string.Empty;
        LayoutMode = PrintLayoutMode.FitToPage;
        NUpCount = NUpPagesPerSheet.Two;
    }

    /// <summary>
    /// 別インスタンスの設定値を複製して反映します。
    /// </summary>
    public void CopyFrom(PrintSettings source)
    {
        if (source == null) return;
        PrinterName = source.PrinterName;
        Copies = source.Copies;
        Orientation = source.Orientation;
        PaperSize = source.PaperSize;
        DuplexMode = source.DuplexMode;
        RangeType = source.RangeType;
        CustomRangeText = source.CustomRangeText;
        LayoutMode = source.LayoutMode;
        NUpCount = source.NUpCount;
    }
}
