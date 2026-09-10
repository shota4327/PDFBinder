using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;

namespace PDFBinder.App.Controls;

/// <summary>
/// スタイラスペン専用のリアルタイムインク描画レンダラー。
/// 手指によるタッチ入力をペンスレッド上でフィルタリングし、不要なゴーストストロークの描画を防止します。
/// </summary>
public class PenOnlyDynamicRenderer : DynamicRenderer
{
    /// <summary>
    /// スタイラス接地時のペンスレッド処理。タッチデバイスからの入力であれば描画処理をスキップします。
    /// </summary>
    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        if (IsTouchInput(rawStylusInput))
        {
            return;
        }

        base.OnStylusDown(rawStylusInput);
    }

    /// <summary>
    /// スタイラス移動時のペンスレッド処理。タッチデバイスからの入力であれば描画処理をスキップします。
    /// </summary>
    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        if (IsTouchInput(rawStylusInput))
        {
            return;
        }

        base.OnStylusMove(rawStylusInput);
    }

    /// <summary>
    /// スタイラス離脱時のペンスレッド処理。タッチデバイスからの入力であれば描画処理をスキップします。
    /// </summary>
    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        if (IsTouchInput(rawStylusInput))
        {
            return;
        }

        base.OnStylusUp(rawStylusInput);
    }

    /// <summary>
    /// 入力元デバイスが手指タッチ（Touch）か判定します。
    /// </summary>
    private static bool IsTouchInput(RawStylusInput rawStylusInput)
    {
        try
        {
            int tabletId = rawStylusInput.TabletDeviceId;
            foreach (TabletDevice device in Tablet.TabletDevices)
            {
                if (device.Id == tabletId)
                {
                    return device.Type == TabletDeviceType.Touch;
                }
            }
        }
        catch
        {
            // ペンスレッドでの例外発生を防止
        }

        return false;
    }
}
