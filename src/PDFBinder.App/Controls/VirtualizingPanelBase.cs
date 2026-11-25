#nullable enable
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PDFBinder.App.Controls;

/// <summary>
/// 祖先 <see cref="ScrollViewer"/> のスクロール位置に連動して表示範囲周辺のコンテナのみを実体化・リサイクルする
/// カスタム <see cref="VirtualizingPanel"/> の共通抽象基底クラス。
/// </summary>
public abstract class VirtualizingPanelBase : VirtualizingPanel
{
    /// <summary>監視中の祖先 ScrollViewer</summary>
    protected ScrollViewer? AttachedScrollViewer { get; private set; }

    /// <summary>現在実体化されている先頭アイテムインデックス</summary>
    protected int RealizedFirstIndex { get; set; } = -1;

    /// <summary>現在実体化されている末尾アイテムインデックス</summary>
    protected int RealizedLastIndex { get; set; } = -1;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    protected VirtualizingPanelBase()
    {
        Loaded += OnPanelLoaded;
        Unloaded += OnPanelUnloaded;
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollViewerAttached();
    }

    private void OnPanelUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScrollViewer();
        OnPanelUnloadedCore();
    }

    /// <summary>
    /// 祖先要素から <see cref="ScrollViewer"/> を検出し、スクロール変更イベントを購読します。
    /// </summary>
    protected void EnsureScrollViewerAttached()
    {
        var scrollViewer = FindAncestorScrollViewer();
        if (ReferenceEquals(AttachedScrollViewer, scrollViewer))
        {
            return;
        }

        DetachScrollViewer();
        AttachedScrollViewer = scrollViewer;
        if (AttachedScrollViewer != null)
        {
            AttachedScrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        }
    }

    /// <summary>
    /// 祖先 <see cref="ScrollViewer"/> からのイベント購読を解除します。
    /// </summary>
    protected void DetachScrollViewer()
    {
        if (AttachedScrollViewer != null)
        {
            AttachedScrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
            AttachedScrollViewer = null;
        }
    }

    private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) < 0.001 &&
            Math.Abs(e.ViewportHeightChange) < 0.001 &&
            Math.Abs(e.ExtentHeightChange) < 0.001 &&
            Math.Abs(e.ViewportWidthChange) < 0.001)
        {
            return;
        }

        if (ShouldInvalidateMeasureOnScroll(e))
        {
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// スクロールまたはビューポート変化時に再計測（InvalidateMeasure）が必要かどうかを判定します。
    /// </summary>
    protected abstract bool ShouldInvalidateMeasureOnScroll(ScrollChangedEventArgs e);

    /// <summary>
    /// コレクション変更通知を受け取った際に派生クラス側で必要な追加処理を実行します。
    /// </summary>
    protected virtual void OnItemsCollectionMutated()
    {
    }

    /// <summary>
    /// パネルアンロード時に派生クラス固有のリソースやイベント購読を解放します。
    /// </summary>
    protected virtual void OnPanelUnloadedCore()
    {
    }

    /// <summary>
    /// 指定された範囲のアイテムコンテナを実体化・計測し、範囲外のコンテナをリサイクルします。
    /// </summary>
    protected void RealizeAndMeasureRange(int firstIndex, int lastIndex, Func<int, Size> itemSizeProvider)
    {
        UIElementCollection children = InternalChildren;
        IItemContainerGenerator? generator = ItemContainerGenerator;
        if (generator == null || firstIndex < 0 || lastIndex < firstIndex)
        {
            CleanUpAllItems();
            return;
        }

        // 先に範囲外コンテナを解放・リサイクルプールへ戻し、同フレーム内の生成で再利用できるようにする
        CleanUpItems(firstIndex, lastIndex);

        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = (startPos.Offset == 0) ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                if (generator.GenerateNext(out bool isNewlyRealized) is not UIElement child)
                {
                    break;
                }

                bool isAlreadyInVisualTree = childIndex < children.Count && ReferenceEquals(children[childIndex], child);
                if (isNewlyRealized || !isAlreadyInVisualTree)
                {
                    if (!isAlreadyInVisualTree)
                    {
                        if (childIndex >= children.Count)
                        {
                            AddInternalChild(child);
                        }
                        else
                        {
                            InsertInternalChild(childIndex, child);
                        }
                    }

                    generator.PrepareItemContainer(child);
                }

                child.Measure(itemSizeProvider(itemIndex));
            }
        }

        RealizedFirstIndex = firstIndex;
        RealizedLastIndex = lastIndex;
    }

    /// <summary>
    /// 指定範囲外の実体化済みコンテナを削除またはリサイクルします。
    /// </summary>
    protected void CleanUpItems(int minDesiredGenerated, int maxDesiredGenerated)
    {
        UIElementCollection children = InternalChildren;
        IItemContainerGenerator? generator = ItemContainerGenerator;
        if (generator == null) return;

        for (int i = children.Count - 1; i >= 0; i--)
        {
            var pos = new GeneratorPosition(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(pos);
            if (itemIndex < 0)
            {
                RemoveInternalChildRange(i, 1);
            }
            else if (itemIndex < minDesiredGenerated || itemIndex > maxDesiredGenerated)
            {
                RecycleOrRemoveContainer(generator, pos);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    /// <summary>
    /// 実体化されているすべてのコンテナを解放・リサイクルします。
    /// </summary>
    protected void CleanUpAllItems()
    {
        UIElementCollection children = InternalChildren;
        IItemContainerGenerator? generator = ItemContainerGenerator;
        if (generator != null)
        {
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var pos = new GeneratorPosition(i, 0);
                int itemIndex = generator.IndexFromGeneratorPosition(pos);
                if (itemIndex >= 0)
                {
                    RecycleOrRemoveContainer(generator, pos);
                }
                RemoveInternalChildRange(i, 1);
            }
        }

        RealizedFirstIndex = -1;
        RealizedLastIndex = -1;
    }

    private static void RecycleOrRemoveContainer(IItemContainerGenerator generator, GeneratorPosition pos)
    {
        if (generator is IRecyclingItemContainerGenerator recycling)
        {
            recycling.Recycle(pos, 1);
        }
        else
        {
            generator.Remove(pos, 1);
        }
    }

    /// <inheritdoc/>
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                RemoveRealizedRangeIfValid(args.Position, args.ItemUICount);
                break;

            case NotifyCollectionChangedAction.Move:
                HandleItemsMoved(args);
                break;

            case NotifyCollectionChangedAction.Reset:
                RealizedFirstIndex = -1;
                RealizedLastIndex = -1;
                break;
        }

        OnItemsCollectionMutated();
        InvalidateMeasure();
        base.OnItemsChanged(sender, args);
    }

    private void RemoveRealizedRangeIfValid(GeneratorPosition position, int count)
    {
        if (position.Offset == 0 && count > 0 &&
            position.Index >= 0 && position.Index + count <= InternalChildren.Count)
        {
            RemoveInternalChildRange(position.Index, count);
        }
    }

    private void HandleItemsMoved(ItemsChangedEventArgs args)
    {
        if (args.ItemUICount <= 0) return;

        bool oldIsRealized = args.OldPosition.Offset == 0 &&
                             args.OldPosition.Index >= 0 &&
                             args.OldPosition.Index + args.ItemUICount <= InternalChildren.Count;
        bool newIsRealized = args.Position.Offset == 0 && args.Position.Index >= 0;

        if (oldIsRealized && newIsRealized)
        {
            MoveRealizedChildren(args.OldPosition.Index, args.Position.Index, args.ItemUICount);
        }
        else if (oldIsRealized)
        {
            RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
        }
    }

    private void MoveRealizedChildren(int oldIndex, int newIndex, int count)
    {
        var moved = new UIElement[count];
        for (int i = 0; i < count; i++)
        {
            moved[i] = InternalChildren[oldIndex + i];
        }

        RemoveInternalChildRange(oldIndex, count);

        int insertBase = Math.Clamp(newIndex, 0, InternalChildren.Count);
        for (int i = 0; i < count; i++)
        {
            int targetIdx = insertBase + i;
            if (targetIdx >= InternalChildren.Count)
            {
                AddInternalChild(moved[i]);
            }
            else
            {
                InsertInternalChild(targetIdx, moved[i]);
            }
        }
    }

    /// <summary>
    /// 指定された子要素インデックスに対応するデータアイテムのコレクションインデックスを取得します。
    /// </summary>
    protected int GetItemIndexForChild(int childIndex)
    {
        IItemContainerGenerator? generator = ItemContainerGenerator;
        return generator != null
            ? generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0))
            : -1;
    }

    private ScrollViewer? FindAncestorScrollViewer()
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current != null)
        {
            if (current is ScrollViewer sv)
            {
                return sv;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
