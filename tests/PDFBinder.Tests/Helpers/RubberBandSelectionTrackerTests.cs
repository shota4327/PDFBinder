using System.Collections.Generic;
using System.Windows;
using PDFBinder.App.Helpers;
using PDFBinder.Core.Models;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="RubberBandSelectionTracker"/> の差分選択更新および Ctrl 修飾キー追従の単体テスト
/// </summary>
public class RubberBandSelectionTrackerTests
{
    private static (List<PdfPageModel> Pages, GridLayoutCache Cache) CreatePagesAndCache(int count, int columns = 4)
    {
        var pages = new List<PdfPageModel>(count);
        var bounds = new List<GridItemBounds>(count);
        for (int i = 0; i < count; i++)
        {
            pages.Add(new PdfPageModel { PageNumber = i + 1 });
            int row = i / columns;
            int col = i % columns;
            bounds.Add(new GridItemBounds(i, new Rect(10 + col * 120, 10 + row * 160, 100, 140)));
        }

        var cache = GridLayoutCache.Build(bounds, new GridLayoutSignature(count, 600, 800, 100));
        return (pages, cache);
    }

    [Fact]
    public void Update_GrowAndShrinkSelection_SelectsAndDeselectsOnlyIntersectingPages()
    {
        var (pages, cache) = CreatePagesAndCache(8, columns: 4);
        var tracker = new RubberBandSelectionTracker();
        tracker.Begin(pages);

        // 拡大: index 0, 1 に交差
        tracker.Update(cache, new Rect(50, 50, 120, 50), isCtrlPressed: false, pages);
        Assert.True(pages[0].IsSelected);
        Assert.True(pages[1].IsSelected);
        Assert.False(pages[2].IsSelected);

        // さらに拡大: index 0, 1, 4, 5 に交差
        tracker.Update(cache, new Rect(50, 50, 120, 160), isCtrlPressed: false, pages);
        Assert.True(pages[0].IsSelected);
        Assert.True(pages[1].IsSelected);
        Assert.True(pages[4].IsSelected);
        Assert.True(pages[5].IsSelected);

        // 縮小: index 0 のみに戻す
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: false, pages);
        Assert.True(pages[0].IsSelected);
        Assert.False(pages[1].IsSelected);
        Assert.False(pages[4].IsSelected);
        Assert.False(pages[5].IsSelected);
    }

    [Fact]
    public void Update_UnchangedPages_DoesNotRaisePropertyChanged()
    {
        var (pages, cache) = CreatePagesAndCache(100, columns: 4);
        int notificationCount = 0;
        foreach (var page in pages)
        {
            page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PdfPageModel.IsSelected))
                {
                    notificationCount++;
                }
            };
        }

        var tracker = new RubberBandSelectionTracker();
        tracker.Begin(pages);

        // index 0, 1 を選択（2件のみ変化）
        int changed1 = tracker.Update(cache, new Rect(50, 50, 120, 50), isCtrlPressed: false, pages);
        Assert.Equal(2, changed1);
        Assert.Equal(2, notificationCount);

        // 同じ2ページのみと交差したまま 1px 移動 → 通知・変更件数ともに 0
        notificationCount = 0;
        int changed2 = tracker.Update(cache, new Rect(51, 51, 122, 52), isCtrlPressed: false, pages);
        Assert.Equal(0, changed2);
        Assert.Equal(0, notificationCount);

        // index 2 まで広げる → 新規の index 2 のみ通知（1件）
        notificationCount = 0;
        int changed3 = tracker.Update(cache, new Rect(50, 50, 250, 50), isCtrlPressed: false, pages);
        Assert.Equal(1, changed3);
        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public void Update_WithCtrlPressed_PreservesInitialSelectionWhenShrinking()
    {
        var (pages, cache) = CreatePagesAndCache(8, columns: 4);
        pages[6].IsSelected = true;

        var tracker = new RubberBandSelectionTracker();
        tracker.Begin(pages);

        // 拡大: index 0, 1 に交差（初期選択の 6 も維持）
        tracker.Update(cache, new Rect(50, 50, 120, 50), isCtrlPressed: true, pages);
        Assert.True(pages[0].IsSelected);
        Assert.True(pages[1].IsSelected);
        Assert.True(pages[6].IsSelected);

        // 縮小して index 1 を外す（0 と初期選択 6 は維持）
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: true, pages);
        Assert.True(pages[0].IsSelected);
        Assert.False(pages[1].IsSelected);
        Assert.True(pages[6].IsSelected);

        // 初期選択 index 6 を矩形で跨いだ後に外しても維持される
        tracker.Update(cache, new Rect(260, 180, 50, 50), isCtrlPressed: true, pages);
        Assert.True(pages[6].IsSelected);
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: true, pages);
        Assert.True(pages[6].IsSelected);
    }

    [Fact]
    public void Update_CtrlToggledMidDrag_RestoresAndClearsInitialSelection()
    {
        var (pages, cache) = CreatePagesAndCache(8, columns: 4);
        pages[5].IsSelected = true;

        var tracker = new RubberBandSelectionTracker();
        tracker.Begin(pages);

        // Ctrl 押下状態で index 0 を選択（5 は初期選択として維持）
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: true, pages);
        Assert.True(pages[0].IsSelected);
        Assert.True(pages[5].IsSelected);

        // ドラッグ途中で Ctrl を離す → 初期選択の index 5 が解除され index 0 のみ残る
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: false, pages);
        Assert.True(pages[0].IsSelected);
        Assert.False(pages[5].IsSelected);

        // ドラッグ途中で再度 Ctrl を押す → 初期選択の index 5 が復元される
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: true, pages);
        Assert.True(pages[0].IsSelected);
        Assert.True(pages[5].IsSelected);
    }

    [Fact]
    public void Update_InitialSelectionWithoutCtrl_MatchesExistingBehavior()
    {
        var (pages, cache) = CreatePagesAndCache(8, columns: 4);
        pages[3].IsSelected = true;
        pages[4].IsSelected = true;

        var tracker = new RubberBandSelectionTracker();
        tracker.Begin(pages);

        // Ctrl なしでドラッグ（Shift 開始等のケース）→ 交差範囲のみ選択（3 は解除、4 と 0 のみ選択）
        tracker.Update(cache, new Rect(50, 50, 20, 150), isCtrlPressed: false, pages);
        Assert.True(pages[0].IsSelected);
        Assert.False(pages[3].IsSelected);
        Assert.True(pages[4].IsSelected);
    }

    [Fact]
    public void Reset_ClearsStateForSubsequentSession()
    {
        var (pages, cache) = CreatePagesAndCache(4, columns: 4);
        pages[3].IsSelected = true;

        var tracker = new RubberBandSelectionTracker();
        tracker.Begin(pages);
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: true, pages);
        tracker.Reset();

        // 全解除して新セッションを開始したとき、以前の初期選択 3 が復活しないこと
        foreach (var p in pages) p.IsSelected = false;
        tracker.Begin(pages);
        tracker.Update(cache, new Rect(50, 50, 20, 20), isCtrlPressed: true, pages);

        Assert.True(pages[0].IsSelected);
        Assert.False(pages[3].IsSelected);
    }
}
