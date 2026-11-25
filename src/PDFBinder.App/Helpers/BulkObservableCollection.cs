#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PDFBinder.App.Helpers;

/// <summary>
/// 大量要素の一括置換時に単一の <see cref="NotifyCollectionChangedAction.Reset"/> 通知のみを発火し、
/// UIバインディングおよびコンテナ生成の通知負荷を最小化する監視可能コレクション。
/// </summary>
/// <typeparam name="T">要素の型</typeparam>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// コレクションの内容を指定された要素群で一括置換し、単一の Reset 通知を発火します。
    /// </summary>
    /// <param name="items">置換後の新しい要素一覧</param>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
