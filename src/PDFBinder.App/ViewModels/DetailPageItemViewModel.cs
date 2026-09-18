using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFBinder.Core.Helpers;
using PDFBinder.Core.Models;

namespace PDFBinder.App.ViewModels;

/// <summary>
/// 詳細エディタにおける各個別ページの表示情報およびレンダリング状態を管理するViewModel
/// </summary>
public partial class DetailPageItemViewModel : ObservableObject
{
    /// <summary>
    /// 指定された差分回転角度で現在保持している背景画像およびストロークキャッシュを即座に幾何回転させます。
    /// これにより、PDFium再レンダリング完了までの間の一時的な引き伸ばしや歪みを完全に防止します。
    /// </summary>
    /// <param name="deltaDegrees">差分回転角度（度単位）</param>
    public void ApplyInstantRotation(int deltaDegrees)
    {
        if (deltaDegrees == 0) return;

        if (PageBackground != null)
        {
            PageBackground = BitmapTransformHelper.CreateRotatedBitmap(PageBackground, deltaDegrees);
            int normalized = ((deltaDegrees % 360) + 360) % 360;
            if (normalized is 90 or 270)
            {
                (LastRenderedWidth, LastRenderedHeight) = (LastRenderedHeight, LastRenderedWidth);
            }
        }

        if (StrokeCache != null)
        {
            StrokeCache = BitmapTransformHelper.CreateRotatedBitmap(StrokeCache, deltaDegrees);
        }
    }
    [ObservableProperty]
    private PdfPageModel _page;

    [ObservableProperty]
    private BitmapSource? _pageBackground;

    /// <summary>
    /// 確定済み手書きストロークの透過ビットマップキャッシュ画像
    /// </summary>
    [ObservableProperty]
    private BitmapSource? _strokeCache;

    /// <summary>
    /// 最後にレンダリングした目標画像幅（px）
    /// </summary>
    public int LastRenderedWidth { get; set; }

    /// <summary>
    /// 最後にレンダリングした目標画像高さ（px）
    /// </summary>
    public int LastRenderedHeight { get; set; }

    /// <summary>
    /// 最後にレンダリングした差分回転角度
    /// </summary>
    public PageRotation LastRenderedRotation { get; set; } = PageRotation.Rotate0;

    /// <summary>
    /// 指定された目標解像度および回転角度ですでにレンダリング済みかどうかを判定します。
    /// </summary>
    public bool IsRenderedAt(int targetWidth, int targetHeight, PageRotation rotation) =>
        PageBackground != null &&
        LastRenderedWidth == targetWidth &&
        LastRenderedHeight == targetHeight &&
        LastRenderedRotation == rotation;

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
    /// ページのテキスト・リンクなどのインタラクティブデータ
    /// </summary>
    [ObservableProperty]
    private PageInteractiveData? _interactiveData;

    /// <summary>
    /// 現在ドラッグ選択されているテキスト
    /// </summary>
    [ObservableProperty]
    private string _selectedText = string.Empty;

    /// <summary>
    /// テキストが選択されているかどうか
    /// </summary>
    [ObservableProperty]
    private bool _hasSelectedText;

    /// <summary>
    /// ページ内リンク等によるジャンプ要求イベント（遷移先ページインデックス）
    /// </summary>
    public event Action<int>? PageJumpRequested;

    partial void OnSelectedTextChanged(string value)
    {
        HasSelectedText = !string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// ページジャンプを要求します。
    /// </summary>
    public void RequestPageJump(int targetPageIndex)
    {
        PageJumpRequested?.Invoke(targetPageIndex);
    }

    /// <summary>
    /// 選択中のテキストをクリップボードにコピーします。
    /// </summary>
    public void CopySelectedText()
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            try
            {
                System.Windows.Clipboard.SetText(SelectedText);
            }
            catch
            {
                // クリップボードロック例外の安全な無視
            }
        }
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="page">対象のPDFページモデル</param>
    public DetailPageItemViewModel(PdfPageModel page)
    {
        _page = page;
    }
}
