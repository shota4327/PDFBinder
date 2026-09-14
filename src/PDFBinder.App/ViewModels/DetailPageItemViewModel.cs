using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFBinder.Core.Models;

namespace PDFBinder.App.ViewModels;

/// <summary>
/// 詳細エディタにおける各個別ページの表示情報およびレンダリング状態を管理するViewModel
/// </summary>
public partial class DetailPageItemViewModel : ObservableObject
{
    [ObservableProperty]
    private PdfPageModel _page;

    [ObservableProperty]
    private BitmapSource? _pageBackground;

    /// <summary>
    /// 確定済み手書きストロークの透過ビットマップキャッシュ画像
    /// </summary>
    [ObservableProperty]
    private BitmapSource? _strokeCache;

    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>
    /// ドキュメント内の先頭ページであるかどうか（連続表示時の上部余白制御用）
    /// </summary>
    [ObservableProperty]
    private bool _isFirstPage;

    /// <summary>
    /// ドキュメント内の最終ページであるかどうか（連続表示時の下部余白制御用）
    /// </summary>
    [ObservableProperty]
    private bool _isLastPage;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="page">対象のPDFページモデル</param>
    public DetailPageItemViewModel(PdfPageModel page)
    {
        _page = page;
    }
}
