#nullable enable
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFBinder.Core.Models;

namespace PDFBinder.App.ViewModels;

/// <summary>
/// 見開き表示における1行（最大2ページ）のレイアウトおよび表示寸法を管理するViewModel
/// </summary>
public sealed class DetailSpreadRowViewModel : ObservableObject, IDisposable
{
    /// <summary>見開き表示時の左右ページ間余白（論理DIP）</summary>
    public const double DefaultPageGap = 16.0;

    private bool _isDisposed;

    /// <summary>0始まりの見開き行インデックス</summary>
    public int RowIndex { get; }

    /// <summary>左側に配置されるページ（空欄時は null）</summary>
    public DetailPageItemViewModel? LeftPage { get; }

    /// <summary>右側に配置されるページ（空欄時は null）</summary>
    public DetailPageItemViewModel? RightPage { get; }

    /// <summary>行内の代表ページ（若い番号側）</summary>
    public DetailPageItemViewModel PrimaryPage { get; }

    /// <summary>単独ページ行を中央の綴じ目に揃えるため反対側スペーサーを確保するかどうか</summary>
    public bool AlignSinglePageToSpine { get; }

    /// <summary>左右ページのいずれも存在するか</summary>
    public bool HasBothPages => LeftPage != null && RightPage != null;

    /// <summary>中央ギャップを表示するかどうか</summary>
    public bool ShowMiddleGap => HasBothPages || (AlignSinglePageToSpine && (LeftPage != null || RightPage != null));

    /// <summary>現在有効なページ間ギャップ（DIP）</summary>
    public double PageGap => ShowMiddleGap ? DefaultPageGap : 0.0;

    /// <summary>左側空欄時に綴じ目位置を揃えるための透明プレースホルダー幅（DIP）</summary>
    public double LeftPlaceholderWidth =>
        AlignSinglePageToSpine && LeftPage == null && RightPage != null
            ? RightPage.Page.DisplayWidth
            : 0.0;

    /// <summary>右側空欄時に綴じ目位置を揃えるための透明プレースホルダー幅（DIP）</summary>
    public double RightPlaceholderWidth =>
        AlignSinglePageToSpine && RightPage == null && LeftPage != null
            ? LeftPage.Page.DisplayWidth
            : 0.0;

    /// <summary>見開き1行分の合計論理幅（DIP）</summary>
    public double DisplayWidth
    {
        get
        {
            if (LeftPage != null && RightPage != null)
            {
                return LeftPage.Page.DisplayWidth + DefaultPageGap + RightPage.Page.DisplayWidth;
            }

            var solo = LeftPage ?? RightPage;
            if (solo == null)
            {
                return 0.0;
            }

            return AlignSinglePageToSpine
                ? solo.Page.DisplayWidth * 2.0 + DefaultPageGap
                : solo.Page.DisplayWidth;
        }
    }

    /// <summary>見開き1行分の最大論理高さ（DIP）</summary>
    public double DisplayHeight => Math.Max(
        LeftPage?.Page.DisplayHeight ?? 0.0,
        RightPage?.Page.DisplayHeight ?? 0.0);

    /// <summary>
    /// 新しい <see cref="DetailSpreadRowViewModel"/> インスタンスを初期化します。
    /// </summary>
    public DetailSpreadRowViewModel(
        int rowIndex,
        DetailPageItemViewModel? leftPage,
        DetailPageItemViewModel? rightPage,
        DetailPageItemViewModel primaryPage,
        bool alignSinglePageToSpine)
    {
        RowIndex = rowIndex;
        LeftPage = leftPage;
        RightPage = rightPage;
        PrimaryPage = primaryPage ?? throw new ArgumentNullException(nameof(primaryPage));
        AlignSinglePageToSpine = alignSinglePageToSpine;

        SubscribePageEvents(LeftPage);
        if (!ReferenceEquals(RightPage, LeftPage))
        {
            SubscribePageEvents(RightPage);
        }
    }

    /// <summary>
    /// 指定されたページモデルがこの見開き行に含まれているかを判定します。
    /// </summary>
    public bool ContainsPage(PdfPageModel? page)
    {
        if (page == null) return false;
        return LeftPage?.Page == page || RightPage?.Page == page;
    }

    /// <summary>
    /// この見開き行に含まれるページアイテム（左→右順）を列挙します。
    /// </summary>
    public IEnumerable<DetailPageItemViewModel> GetPages()
    {
        if (LeftPage != null) yield return LeftPage;
        if (RightPage != null && !ReferenceEquals(RightPage, LeftPage)) yield return RightPage;
    }

    private void SubscribePageEvents(DetailPageItemViewModel? item)
    {
        if (item != null)
        {
            item.Page.PropertyChanged += OnMemberPagePropertyChanged;
        }
    }

    private void UnsubscribePageEvents(DetailPageItemViewModel? item)
    {
        if (item != null)
        {
            item.Page.PropertyChanged -= OnMemberPagePropertyChanged;
        }
    }

    private void OnMemberPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PdfPageModel.DisplayWidth) or nameof(PdfPageModel.DisplayHeight))
        {
            OnPropertyChanged(nameof(LeftPlaceholderWidth));
            OnPropertyChanged(nameof(RightPlaceholderWidth));
            OnPropertyChanged(nameof(DisplayWidth));
            OnPropertyChanged(nameof(DisplayHeight));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_isDisposed) return;
        UnsubscribePageEvents(LeftPage);
        if (!ReferenceEquals(RightPage, LeftPage))
        {
            UnsubscribePageEvents(RightPage);
        }
        _isDisposed = true;
    }
}
