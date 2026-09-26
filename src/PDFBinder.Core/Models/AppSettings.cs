namespace PDFBinder.Core.Models;

/// <summary>
/// アプリケーション全体の設定情報を保持するモデルクラス
/// </summary>
public class AppSettings
{
    /// <summary>
    /// ウィンドウの表示状態およびサイズに関する設定
    /// </summary>
    public WindowSettings Window { get; set; } = new();

    /// <summary>
    /// ディスプレイ構成プロファイルごとの個別ウィンドウ設定（キー: プロファイル識別子）
    /// </summary>
    public Dictionary<string, WindowSettings> DisplayProfiles { get; set; } = new();
}

/// <summary>
/// ウィンドウサイズおよび最大化状態の設定
/// </summary>
public class WindowSettings
{
    /// <summary>
    /// ウィンドウの幅（ピクセル単位、初期値: 1100）
    /// </summary>
    public double Width { get; set; } = 1100;

    /// <summary>
    /// ウィンドウの高さ（ピクセル単位、初期値: 760）
    /// </summary>
    public double Height { get; set; } = 760;

    /// <summary>
    /// 最大化状態で起動するかどうかのフラグ（初期値: false）
    /// </summary>
    public bool IsMaximized { get; set; } = false;
}
