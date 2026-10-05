using CommunityToolkit.Mvvm.ComponentModel;

namespace PDFBinder.Core.Models;

/// <summary>
/// 画像書き出し設定を保持するモデルクラス
/// </summary>
public partial class ImageExportSettings : ObservableObject
{
    [ObservableProperty]
    private ImageExportFormat _format = ImageExportFormat.Png;

    [ObservableProperty]
    private ImageExportDpi _dpi = ImageExportDpi.Dpi400;

    [ObservableProperty]
    private ImageExportRangeType _rangeType = ImageExportRangeType.AllPages;

    [ObservableProperty]
    private string _customRangeText = string.Empty;

    /// <summary>
    /// 設定内容をデフォルト値にリセットします。
    /// </summary>
    public void ResetToDefault()
    {
        Format = ImageExportFormat.Png;
        Dpi = ImageExportDpi.Dpi400;
        RangeType = ImageExportRangeType.AllPages;
        CustomRangeText = string.Empty;
    }

    /// <summary>
    /// 別インスタンスの設定値を複製して反映します。
    /// </summary>
    /// <param name="source">複製元の設定インスタンス</param>
    public void CopyFrom(ImageExportSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Format = source.Format;
        Dpi = source.Dpi;
        RangeType = source.RangeType;
        CustomRangeText = source.CustomRangeText;
    }
}
