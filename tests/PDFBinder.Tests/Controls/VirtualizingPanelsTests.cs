#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.App.Controls;
using PDFBinder.App.Models;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests.Controls;

/// <summary>
/// <see cref="VirtualizingPageStackPanel"/> および <see cref="VirtualizingThumbnailWrapPanel"/> の
/// UI仮想化、スクロール追従、コンテナリサイクル、およびコレクション変更追従の単体テスト
/// </summary>
public class VirtualizingPanelsTests
{
    [Fact]
    public void VirtualizingPageStackPanel_300Pages_RealizesSubsetAndShiftsOnScroll()
    {
        RunOnStaThread(() =>
        {
            var items = new ObservableCollection<DetailPageItemViewModel>();
            for (int i = 0; i < 300; i++)
            {
                items.Add(new DetailPageItemViewModel(new PdfPageModel
                {
                    PageNumber = i + 1,
                    Width = 595.28,
                    Height = 841.89
                }));
            }

            var factory = new FrameworkElementFactory(typeof(VirtualizingPageStackPanel));
            var itemsControl = new ItemsControl
            {
                ItemsSource = items,
                ItemsPanel = new ItemsPanelTemplate(factory),
                ItemTemplate = CreateFixedSizeTemplate(595.28, 841.89)
            };
            VirtualizingPanel.SetIsVirtualizing(itemsControl, true);
            VirtualizingPanel.SetVirtualizationMode(itemsControl, VirtualizationMode.Recycling);

            var scrollViewer = new ScrollViewer
            {
                Width = 800,
                Height = 800,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = itemsControl
            };

            scrollViewer.Measure(new Size(800, 800));
            scrollViewer.Arrange(new Rect(0, 0, 800, 800));
            scrollViewer.UpdateLayout();

            var panel = FindVisualChild<VirtualizingPageStackPanel>(itemsControl);
            Assert.NotNull(panel);
            Assert.True(panel!.Children.Count > 0, "先頭付近のコンテナが実体化されていません");
            Assert.True(panel.Children.Count < 15, $"仮想化コンテナ数が多すぎます: {panel.Children.Count}");

            int initialFirstIndex = GetRealizedIndices(itemsControl, panel)[0];
            Assert.Equal(0, initialFirstIndex);

            // 中盤へ大きくスクロールして実体化範囲が追従することを確認
            scrollViewer.ScrollToVerticalOffset(50000);
            scrollViewer.UpdateLayout();

            Assert.True(panel.Children.Count > 0);
            Assert.True(panel.Children.Count < 15, $"スクロール後の仮想化コンテナ数が多すぎます: {panel.Children.Count}");
            var scrolledIndices = GetRealizedIndices(itemsControl, panel);
            Assert.True(scrolledIndices[0] > 40, $"スクロール後にインデックスが遷移していません: {scrolledIndices[0]}");
        });
    }

    [Fact]
    public void VirtualizingThumbnailWrapPanel_500Pages_RealizesSubsetAndHandlesMutations()
    {
        RunOnStaThread(() =>
        {
            var pages = new ObservableCollection<PdfPageModel>();
            for (int i = 0; i < 500; i++)
            {
                pages.Add(new PdfPageModel
                {
                    PageNumber = i + 1,
                    Width = 595.28,
                    Height = 841.89
                });
            }

            var factory = new FrameworkElementFactory(typeof(VirtualizingThumbnailWrapPanel));
            var itemsControl = new ItemsControl
            {
                ItemsSource = pages,
                ItemsPanel = new ItemsPanelTemplate(factory),
                ItemTemplate = CreateFixedSizeTemplate(220, 311)
            };
            VirtualizingPanel.SetIsVirtualizing(itemsControl, true);
            VirtualizingPanel.SetVirtualizationMode(itemsControl, VirtualizationMode.Recycling);

            var scrollViewer = new ScrollViewer
            {
                Width = 1040,
                Height = 700,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = itemsControl
            };

            scrollViewer.Measure(new Size(1040, 700));
            scrollViewer.Arrange(new Rect(0, 0, 1040, 700));
            scrollViewer.UpdateLayout();

            var panel = FindVisualChild<VirtualizingThumbnailWrapPanel>(itemsControl);
            Assert.NotNull(panel);
            Assert.True(panel!.Children.Count > 0);
            Assert.True(panel.Children.Count < 40, $"仮想化サムネイル数が多すぎます: {panel.Children.Count}");
            Assert.Equal(500, panel.BuildAllItemBounds(scrollViewer).Count);

            // スクロール追従検証
            scrollViewer.ScrollToVerticalOffset(10000);
            scrollViewer.UpdateLayout();
            Assert.True(panel.Children.Count < 40);
            var scrolledIndices = GetRealizedIndices(itemsControl, panel);
            Assert.True(scrolledIndices[0] > 50);

            // 先頭付近へ戻し、Move / Remove / Insert / Clear の各コレクション変更に安全追従できること
            scrollViewer.ScrollToVerticalOffset(0);
            scrollViewer.UpdateLayout();

            pages.Move(0, 3);
            scrollViewer.UpdateLayout();
            Assert.Equal(500, panel.CurrentLayout.Count);

            pages.RemoveAt(0);
            scrollViewer.UpdateLayout();
            Assert.Equal(499, panel.CurrentLayout.Count);

            pages.Insert(0, new PdfPageModel { PageNumber = 999 });
            scrollViewer.UpdateLayout();
            Assert.Equal(500, panel.CurrentLayout.Count);

            pages.Clear();
            scrollViewer.UpdateLayout();
            Assert.Equal(0, panel.CurrentLayout.Count);
            Assert.Empty(panel.Children);
        });
    }

    [Fact]
    public void DetailEditorViewModel_InitializeDocument_500Pages_FiresSingleResetAndTracksIsContinuousMode()
    {
        var doc = new PdfDocumentModel();
        for (int i = 0; i < 500; i++)
        {
            doc.AddPage(new PdfPageModel { PageNumber = i + 1 });
        }

        using var vm = new DetailEditorViewModel(new DummyPdfRenderer(), autoLoadBackground: false);
        var actions = new List<NotifyCollectionChangedAction>();
        vm.Pages.CollectionChanged += (_, e) => actions.Add(e.Action);

        vm.InitializeDocument(doc, autoLoadBackground: false);

        Assert.Single(actions);
        Assert.Equal(NotifyCollectionChangedAction.Reset, actions[0]);
        Assert.Equal(500, vm.Pages.Count);
        Assert.True(vm.Pages[0].IsFirstPage);
        Assert.True(vm.Pages[499].IsLastPage);

        var changedProps = new List<string?>();
        vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        Assert.False(vm.IsContinuousMode);
        vm.PageViewMode = DetailPageViewMode.Continuous;
        Assert.True(vm.IsContinuousMode);
        Assert.Contains(nameof(DetailEditorViewModel.IsContinuousMode), changedProps);

        vm.PageViewMode = DetailPageViewMode.SinglePage;
        Assert.False(vm.IsContinuousMode);
    }

    [Fact]
    public void VirtualizingThumbnailWrapPanel_200Pages_RepeatedUpDownScrolling_PreservesAllVisibleContainersAndBindings()
    {
        RunOnStaThread(() =>
        {
            var pages = new ObservableCollection<PdfPageModel>();
            for (int i = 0; i < 200; i++)
            {
                pages.Add(new PdfPageModel
                {
                    PageNumber = i + 1,
                    Width = 595.28,
                    Height = 841.89
                });
            }

            var factory = new FrameworkElementFactory(typeof(VirtualizingThumbnailWrapPanel));
            var itemsControl = new ItemsControl
            {
                ItemsSource = pages,
                ItemsPanel = new ItemsPanelTemplate(factory),
                ItemTemplate = CreateFixedSizeTemplate(220, 311)
            };
            VirtualizingPanel.SetIsVirtualizing(itemsControl, true);
            VirtualizingPanel.SetVirtualizationMode(itemsControl, VirtualizationMode.Recycling);

            var scrollViewer = new ScrollViewer
            {
                Width = 1040,
                Height = 700,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = itemsControl
            };

            scrollViewer.Measure(new Size(1040, 700));
            scrollViewer.Arrange(new Rect(0, 0, 1040, 700));
            scrollViewer.UpdateLayout();

            var panel = FindVisualChild<VirtualizingThumbnailWrapPanel>(itemsControl);
            Assert.NotNull(panel);

            double maxOffset = scrollViewer.ScrollableHeight;
            double[] offsets =
            {
                0, 500, 1200, 2500, 4500, 7500, 11000, 14500, maxOffset,
                14500, 11000, 7500, 4500, 2500, 1200, 500, 0, 6000, 1000
            };

            foreach (double rawOffset in offsets)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Clamp(rawOffset, 0, maxOffset));
                scrollViewer.UpdateLayout();

                Assert.True(panel!.Children.Count > 0, $"Offset={rawOffset}: 実体化コンテナが空です");
                var realizedIndices = GetRealizedIndices(itemsControl, panel);
                AssertContiguousAndBoundCorrectly(panel, realizedIndices, pages);

                double visTop = scrollViewer.VerticalOffset;
                double visBottom = visTop + scrollViewer.ViewportHeight;
                for (int p = 0; p < pages.Count; p++)
                {
                    var rect = panel.CurrentLayout.GetItemRect(p);
                    if (rect.Bottom >= visTop && rect.Top <= visBottom)
                    {
                        Assert.InRange(p, realizedIndices[0], realizedIndices[^1]);
                    }
                }
            }
        });
    }

    [Fact]
    public void VirtualizingPageStackPanel_200Pages_RepeatedUpDownScrolling_PreservesAllVisibleContainersAndBindings()
    {
        RunOnStaThread(() =>
        {
            var items = new ObservableCollection<DetailPageItemViewModel>();
            for (int i = 0; i < 200; i++)
            {
                items.Add(new DetailPageItemViewModel(new PdfPageModel
                {
                    PageNumber = i + 1,
                    Width = 595.28,
                    Height = 841.89
                }));
            }

            var factory = new FrameworkElementFactory(typeof(VirtualizingPageStackPanel));
            var itemsControl = new ItemsControl
            {
                ItemsSource = items,
                ItemsPanel = new ItemsPanelTemplate(factory),
                ItemTemplate = CreateFixedSizeTemplate(595.28, 841.89)
            };
            VirtualizingPanel.SetIsVirtualizing(itemsControl, true);
            VirtualizingPanel.SetVirtualizationMode(itemsControl, VirtualizationMode.Recycling);

            var scrollViewer = new ScrollViewer
            {
                Width = 800,
                Height = 800,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = itemsControl
            };

            scrollViewer.Measure(new Size(800, 800));
            scrollViewer.Arrange(new Rect(0, 0, 800, 800));
            scrollViewer.UpdateLayout();

            var panel = FindVisualChild<VirtualizingPageStackPanel>(itemsControl);
            Assert.NotNull(panel);

            double maxOffset = scrollViewer.ScrollableHeight;
            double[] offsets =
            {
                0, 2000, 10000, 30000, 70000, 120000, maxOffset,
                120000, 70000, 30000, 10000, 2000, 0, 50000, 8000
            };

            foreach (double rawOffset in offsets)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Clamp(rawOffset, 0, maxOffset));
                scrollViewer.UpdateLayout();

                Assert.True(panel!.Children.Count > 0, $"Offset={rawOffset}: 実体化コンテナが空です");
                var realizedIndices = GetRealizedIndices(itemsControl, panel);
                AssertContiguousAndBoundCorrectly(panel, realizedIndices, items);

                double visTop = scrollViewer.VerticalOffset;
                double visBottom = visTop + scrollViewer.ViewportHeight;
                for (int p = 0; p < items.Count; p++)
                {
                    double top = panel.TopMargin + panel.CurrentLayout.GetPageTop(p);
                    double bottom = top + panel.CurrentLayout.GetPageHeight(p);
                    if (bottom >= visTop && top <= visBottom)
                    {
                        Assert.InRange(p, realizedIndices[0], realizedIndices[^1]);
                    }
                }
            }
        });
    }

    private static void AssertContiguousAndBoundCorrectly<T>(Panel panel, IReadOnlyList<int> realizedIndices, IReadOnlyList<T> source) where T : class
    {
        Assert.Equal(panel.Children.Count, realizedIndices.Count);
        for (int i = 0; i < realizedIndices.Count; i++)
        {
            int expectedIndex = realizedIndices[0] + i;
            Assert.Equal(expectedIndex, realizedIndices[i]);

            var cp = Assert.IsType<ContentPresenter>(panel.Children[i]);
            Assert.Same(source[expectedIndex], cp.Content);
            Assert.True(cp.RenderSize.Width > 0 && cp.RenderSize.Height > 0, $"Index={expectedIndex} のコンテナサイズが0です");
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(10000);
        Assert.True(finished, "STAスレッドがタイムアウトしました");

        if (captured != null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }

    private static DataTemplate CreateFixedSizeTemplate(double width, double height)
    {
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(FrameworkElement.WidthProperty, width);
        borderFactory.SetValue(FrameworkElement.HeightProperty, height);
        return new DataTemplate { VisualTree = borderFactory };
    }

    private static List<int> GetRealizedIndices(ItemsControl itemsControl, Panel panel)
    {
        var list = new List<int>(panel.Children.Count);
        var gen = (IItemContainerGenerator)itemsControl.ItemContainerGenerator;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            int idx = gen.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            list.Add(idx);
        }
        return list;
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }
            var nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    private sealed class DummyPdfRenderer : IPdfRenderer
    {
        public Task<BitmapSource?> RenderPageAsync(
            string? filePath, int pageIndex, int targetWidth, int targetHeight, PageRotation rotation, CancellationToken cancellationToken = default)
            => Task.FromResult<BitmapSource?>(null);

        public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
            => BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);

        public BitmapSource CompositeStrokes(BitmapSource baseImage, StrokeCollection strokes, double originalPageWidth, double originalPageHeight)
            => baseImage;

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(
            string? filePath, int pageIndex, double displayWidth, double displayHeight, PageRotation rotation, CancellationToken cancellationToken = default)
            => Task.FromResult(PageInteractiveData.Empty);
    }
}
