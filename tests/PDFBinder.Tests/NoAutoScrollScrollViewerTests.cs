using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PDFBinder.App.Controls;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="NoAutoScrollScrollViewer"/> の自動スクロール抑止機能のテスト
/// </summary>
public class NoAutoScrollScrollViewerTests
{
    [Fact]
    public void NoAutoScrollScrollViewer_OnRequestBringIntoView_MarksHandledAndPreventsScroll()
    {
        var thread = new Thread(() =>
        {
            var scrollViewer = new NoAutoScrollScrollViewer
            {
                Width = 200,
                Height = 200,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var panel = new StackPanel { Width = 1000, Height = 1000 };
            var child = new Border { Width = 100, Height = 100, Margin = new Thickness(400, 400, 0, 0) };
            panel.Children.Add(child);
            scrollViewer.Content = panel;

            // レイアウトを強制計測・配置
            scrollViewer.Measure(new Size(200, 200));
            scrollViewer.Arrange(new Rect(0, 0, 200, 200));

            Assert.Equal(0, scrollViewer.HorizontalOffset);
            Assert.Equal(0, scrollViewer.VerticalOffset);

            // 子要素からBringIntoViewを要求
            child.BringIntoView();

            // 自動スクロールが発生せず、オフセットが0のまま維持されていることを確認
            Assert.Equal(0, scrollViewer.HorizontalOffset);
            Assert.Equal(0, scrollViewer.VerticalOffset);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    [Theory]
    [InlineData(Key.PageUp)]
    [InlineData(Key.PageDown)]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    public void NoAutoScrollScrollViewer_OnKeyDown_SuppressesInternalHandlingForPageNavigationKeys(Key key)
    {
        var thread = new Thread(() =>
        {
            var scrollViewer = new TestableNoAutoScrollScrollViewer();
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "", nint.Zero), 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };

            scrollViewer.TestOnKeyDown(e);

            Assert.False(e.Handled, $"Key {key} should not be handled by NoAutoScrollScrollViewer");
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool finished = thread.Join(5000);
        Assert.True(finished, "Thread timed out");
    }

    private class TestableNoAutoScrollScrollViewer : NoAutoScrollScrollViewer
    {
        public void TestOnKeyDown(KeyEventArgs e) => OnKeyDown(e);
    }
}
