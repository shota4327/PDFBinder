using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    /// <summary>
    /// ページ移動ショートカットに割り当てられたキー（PageUp, PageDown, Up, Down, Left, Right）の内部消費を抑止し、
    /// ウィンドウレベルでのショートカット実行を保証します。
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.PageUp or Key.PageDown or Key.Up or Key.Down or Key.Left or Key.Right)
        {
            return;
        }

        base.OnKeyDown(e);
    }
}
