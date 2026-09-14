using System.Windows;
using System.Windows.Controls;

namespace PDFBinder.App.Controls;

/// <summary>
/// 子要素のフォーカス取得やBringIntoView呼び出しによる意図しない自動スクロールを完全に抑止するScrollViewer。
/// </summary>
public class NoAutoScrollScrollViewer : ScrollViewer
{
    static NoAutoScrollScrollViewer()
    {
        EventManager.RegisterClassHandler(
            typeof(NoAutoScrollScrollViewer),
            FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(OnRequestBringIntoView),
            handledEventsToo: true);
    }

    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // クラスハンドラー段階でHandledを設定し、内部の自動引き込みスクロールロジックを無力化します。
        e.Handled = true;
    }
}
