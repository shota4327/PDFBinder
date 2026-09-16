namespace PDFBinder.Core.Helpers;

/// <summary>
/// WinTab APIから取得した生の筆圧値をWPFのPressureFactor（0.05〜1.0）へ正規化するヘルパークラス。
/// </summary>
public static class WinTabPressureHelper
{
    /// <summary>軽いタッチ時の描画途切れを防止するための最小筆圧係数</summary>
    public const float MinPressureFactor = 0.05f;

    /// <summary>最大筆圧係数</summary>
    public const float MaxPressureFactor = 1.0f;

    /// <summary>筆圧情報が無効な場合のデフォルト筆圧係数（WPF標準等倍値）</summary>
    public const float DefaultPressureFactor = 0.5f;

    /// <summary>
    /// 生の筆圧値（0〜maxPressure）をWPF StylusPoint用の0.05〜1.0の範囲に線形正規化します。
    /// </summary>
    /// <param name="rawPressure">WinTabから取得された現在の筆圧値</param>
    /// <param name="maxPressure">タブレットデバイスがサポートする最大筆圧値</param>
    /// <returns>0.05〜1.0の範囲にクランプされた筆圧係数（PressureFactor）</returns>
    public static float NormalizePressure(int rawPressure, int maxPressure)
    {
        if (maxPressure <= 0)
        {
            return DefaultPressureFactor;
        }

        if (rawPressure <= 0)
        {
            return MinPressureFactor;
        }

        if (rawPressure >= maxPressure)
        {
            return MaxPressureFactor;
        }

        float normalized = (float)rawPressure / maxPressure;
        return Math.Clamp(normalized, MinPressureFactor, MaxPressureFactor);
    }
}
