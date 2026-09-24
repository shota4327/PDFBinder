using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PDFBinder.App;
using PDFBinder.App.Controls;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="OverlayScrollViewerStyle"/> およびモダンスクロールバーのテンプレート構造の検証テスト
/// </summary>
public class OverlayScrollViewerStyleTests
{
    [Fact]
    public void OverlayScrollViewerStyle_InstantiatesHorizontalAndVerticalScrollBarsWithOffset()
    {
        var thread = new Thread(() =>
        {
            // App.xamlのリソースを確実にロード
            if (Application.Current == null)
            {
                var app = new PDFBinder.App.App();
                app.InitializeComponent();
            }

            var style = Application.Current!.FindResource("OverlayScrollViewerStyle") as Style;
            Assert.NotNull(style);

            var scrollViewer = new NoAutoScrollScrollViewer
            {
                Style = style,
                Width = 400,
                Height = 300,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible
            };

            var content = new Border { Width = 800, Height = 600 };
            scrollViewer.Content = content;

            // テンプレートを適用し、レイアウトを計測
            scrollViewer.Measure(new Size(400, 300));
            scrollViewer.Arrange(new Rect(0, 0, 400, 300));
            scrollViewer.ApplyTemplate();

            // テンプレート子要素の取得
            var hScrollBar = scrollViewer.Template.FindName("PART_HorizontalScrollBar", scrollViewer) as ScrollBar;
            var vScrollBar = scrollViewer.Template.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar;
            var contentPresenter = scrollViewer.Template.FindName("PART_ScrollContentPresenter", scrollViewer) as ScrollContentPresenter;

            Assert.NotNull(hScrollBar);
            Assert.NotNull(vScrollBar);
            Assert.NotNull(contentPresenter);

            // スクロールバーの方向検証
            Assert.Equal(Orientation.Horizontal, hScrollBar.Orientation);
            Assert.Equal(Orientation.Vertical, vScrollBar.Orientation);

            // 下部ステータスバー回避スペーサー（Row 1: 42px）の存在確認
            var scrollBarGrid = (hScrollBar.Parent as FrameworkElement)?.Parent as Grid;
            Assert.NotNull(scrollBarGrid);
            Assert.Equal(2, scrollBarGrid.RowDefinitions.Count);
            Assert.Equal(42.0, scrollBarGrid.RowDefinitions[1].Height.Value);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }
}
