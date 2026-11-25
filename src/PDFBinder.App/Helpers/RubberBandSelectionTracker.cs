#nullable enable
using System.Collections.Generic;
using System.Windows;
using PDFBinder.Core.Models;

namespace PDFBinder.App.Helpers;

/// <summary>
/// ラバーバンド矩形選択において、前フレームとの交差集合の差分のみを追跡し、
/// 選択状態が変化したページの <see cref="PdfPageModel.IsSelected"/> のみを更新するトラッカー。
/// </summary>
public sealed class RubberBandSelectionTracker
{
    private readonly HashSet<int> _initialSelectedIndices = new();
    private HashSet<int> _previousIntersectingIndices = new();
    private HashSet<int> _currentIntersectingIndices = new();
    private bool _isInitialSelectionApplied;

    /// <summary>
    /// ラバーバンド選択セッションを開始し、開始時点で選択されているページインデックスを記録します。
    /// </summary>
    /// <param name="pages">現在のドキュメントのページ一覧</param>
    public void Begin(IReadOnlyList<PdfPageModel> pages)
    {
        Reset();
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i].IsSelected)
            {
                _initialSelectedIndices.Add(i);
            }
        }

        _isInitialSelectionApplied = true;
    }

    /// <summary>
    /// 現在の選択矩形と Ctrl 修飾キー状態に基づいて、交差状態が変化したページのみ選択を更新します。
    /// </summary>
    /// <param name="layoutCache">キャッシュ済みグリッドレイアウト</param>
    /// <param name="selectionRect">現在のラバーバンド矩形</param>
    /// <param name="isCtrlPressed">Ctrl キーが押下中かどうか</param>
    /// <param name="pages">更新対象のページ一覧</param>
    /// <returns>実際に <see cref="PdfPageModel.IsSelected"/> を変更したページ数</returns>
    public int Update(
        GridLayoutCache layoutCache,
        Rect selectionRect,
        bool isCtrlPressed,
        IReadOnlyList<PdfPageModel> pages)
    {
        layoutCache.FindIntersectingIndices(selectionRect, _currentIntersectingIndices);
        int updatedCount = 0;

        if (_isInitialSelectionApplied != isCtrlPressed)
        {
            updatedCount += ApplyInitialSelectionTransition(isCtrlPressed, pages);
            _isInitialSelectionApplied = isCtrlPressed;
        }

        updatedCount += DeselectExitedItems(isCtrlPressed, pages);
        updatedCount += SelectEnteredItems(pages);

        (_previousIntersectingIndices, _currentIntersectingIndices) =
            (_currentIntersectingIndices, _previousIntersectingIndices);

        return updatedCount;
    }

    /// <summary>
    /// セッション状態を完全にリセットします。
    /// </summary>
    public void Reset()
    {
        _initialSelectedIndices.Clear();
        _previousIntersectingIndices.Clear();
        _currentIntersectingIndices.Clear();
        _isInitialSelectionApplied = false;
    }

    /// <summary>
    /// ドラッグ中の Ctrl キー押下状態変化に応じて、初期選択ページの選択・解除を切り替えます。
    /// </summary>
    private int ApplyInitialSelectionTransition(bool isCtrlPressed, IReadOnlyList<PdfPageModel> pages)
    {
        int updatedCount = 0;
        foreach (int index in _initialSelectedIndices)
        {
            if ((uint)index >= (uint)pages.Count)
            {
                continue;
            }

            if (isCtrlPressed)
            {
                if (!pages[index].IsSelected)
                {
                    pages[index].IsSelected = true;
                    updatedCount++;
                }
            }
            else if (!_currentIntersectingIndices.Contains(index) && pages[index].IsSelected)
            {
                pages[index].IsSelected = false;
                updatedCount++;
            }
        }

        return updatedCount;
    }

    /// <summary>
    /// 前フレームで交差していたが今フレームで選択矩形外へ出たページの選択状態を更新します。
    /// </summary>
    private int DeselectExitedItems(bool isCtrlPressed, IReadOnlyList<PdfPageModel> pages)
    {
        int updatedCount = 0;
        foreach (int index in _previousIntersectingIndices)
        {
            if (_currentIntersectingIndices.Contains(index) || (uint)index >= (uint)pages.Count)
            {
                continue;
            }

            bool shouldRemainSelected = isCtrlPressed && _initialSelectedIndices.Contains(index);
            if (pages[index].IsSelected != shouldRemainSelected)
            {
                pages[index].IsSelected = shouldRemainSelected;
                updatedCount++;
            }
        }

        return updatedCount;
    }

    /// <summary>
    /// 今フレームで新たに選択矩形と交差したページを選択状態にします。
    /// </summary>
    private int SelectEnteredItems(IReadOnlyList<PdfPageModel> pages)
    {
        int updatedCount = 0;
        foreach (int index in _currentIntersectingIndices)
        {
            if (_previousIntersectingIndices.Contains(index) || (uint)index >= (uint)pages.Count)
            {
                continue;
            }

            if (!pages[index].IsSelected)
            {
                pages[index].IsSelected = true;
                updatedCount++;
            }
        }

        return updatedCount;
    }
}
