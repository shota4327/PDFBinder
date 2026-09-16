using System.Windows;

namespace PDFBinder.App.Interop.WinTab;

/// <summary>
/// WinTabパケットの種類を表す列挙体。
/// </summary>
public enum WinTabPacketType
{
    /// <summary>ペン先が接地した瞬間</summary>
    Down,
    /// <summary>ペン先が接地したまま移動中</summary>
    Move,
    /// <summary>ペン先が画面から離脱した瞬間</summary>
    Up
}

/// <summary>
/// WinTabパケット受信イベント引数。
/// </summary>
public class WinTabPacketEventArgs : EventArgs
{
    /// <summary>Windowsスクリーン座標（ピクセル単位）</summary>
    public Point ScreenPoint { get; }

    /// <summary>生の筆圧値</summary>
    public int RawPressure { get; }

    /// <summary>0.05〜1.0に正規化されたWPF筆圧係数</summary>
    public float PressureFactor { get; }

    /// <summary>パケットのイベント種別</summary>
    public WinTabPacketType PacketType { get; }

    public WinTabPacketEventArgs(Point screenPoint, int rawPressure, float pressureFactor, WinTabPacketType packetType)
    {
        ScreenPoint = screenPoint;
        RawPressure = rawPressure;
        PressureFactor = pressureFactor;
        PacketType = packetType;
    }
}

/// <summary>
/// WinTab API連携サービスのインターフェース。
/// </summary>
public interface IWinTabService : IDisposable
{
    /// <summary>WinTabが利用可能かつコンテキストが開いているかを取得します。</summary>
    bool IsAvailable { get; }

    /// <summary>検出されたタブレットデバイス名を取得します。</summary>
    string DeviceName { get; }

    /// <summary>最大筆圧レベルを取得します。</summary>
    int MaxPressure { get; }

    /// <summary>現在ペン先が接地中かを取得します。</summary>
    bool IsPenDown { get; }

    /// <summary>WinTabパケット受信イベント</summary>
    event EventHandler<WinTabPacketEventArgs>? PacketReceived;

    /// <summary>
    /// 指定されたウィンドウハンドルでWinTabコンテキストを初期化します。
    /// </summary>
    /// <param name="hwnd">対象ウィンドウのハンドル</param>
    /// <returns>初期化に成功した場合はtrue、失敗または非サポート環境ではfalse</returns>
    bool Initialize(IntPtr hwnd);

    /// <summary>
    /// WinTabコンテキストをクローズし、フックを解除します。
    /// </summary>
    void Close();
}
