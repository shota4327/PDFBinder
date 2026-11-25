#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests.Helpers;

/// <summary>
/// <see cref="BulkObservableCollection{T}"/> の一括置換および通知回数の単体テスト
/// </summary>
public class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_PopulatesAllItemsAndFiresSingleResetNotification()
    {
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        var actions = new List<NotifyCollectionChangedAction>();
        var propertyNames = new List<string?>();

        collection.CollectionChanged += (_, e) => actions.Add(e.Action);
        ((System.ComponentModel.INotifyPropertyChanged)collection).PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName);

        collection.ReplaceAll(new[] { 10, 20, 30, 40, 50 });

        Assert.Equal(new[] { 10, 20, 30, 40, 50 }, collection);
        Assert.Single(actions);
        Assert.Equal(NotifyCollectionChangedAction.Reset, actions[0]);
        Assert.Contains("Count", propertyNames);
        Assert.Contains("Item[]", propertyNames);
    }

    [Fact]
    public void ReplaceAll_FromEmptyAndToEmpty_HandlesBoundariesCorrectly()
    {
        var collection = new BulkObservableCollection<string>();
        int eventCount = 0;
        collection.CollectionChanged += (_, _) => eventCount++;

        collection.ReplaceAll(new[] { "a", "b" });
        Assert.Equal(2, collection.Count);
        Assert.Equal(1, eventCount);

        collection.ReplaceAll(Array.Empty<string>());
        Assert.Empty(collection);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void ReplaceAll_NullItems_ThrowsArgumentNullException()
    {
        var collection = new BulkObservableCollection<int>();
        Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null!));
    }
}
