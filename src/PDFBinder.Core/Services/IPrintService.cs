using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// プリンター印刷設定ダイアログでの設定結果
/// </summary>
/// <param name="DevModeData">更新されたDEVMODEバイナリデータ（ドライバー固有情報を含む）</param>
/// <param name="PaperSize">用紙サイズ（判定できた場合）</param>
/// <param name="Orientation">用紙の向き（判定できた場合）</param>
/// <param name="Copies">部数（判定できた場合）</param>
/// <param name="DuplexMode">両面印刷設定（判定できた場合）</param>
public record PrinterSettingsDialogResult(
    byte[] DevModeData,
    PrintPaperSize? PaperSize,
    PrintOrientation? Orientation,
    int? Copies,
    PrintDuplexMode? DuplexMode);

/// <summary>
/// プリンター操作およびドキュメント印刷を行うインターフェース
/// </summary>
public interface IPrintService
{
    /// <summary>
    /// プリンターのプロパティ（印刷設定）ダイアログをモーダル表示し、ユーザーが変更した設定を取得します。
    /// </summary>
    /// <param name="printerName">対象のプリンター名</param>
    /// <param name="ownerHwnd">親ウィンドウのハンドル</param>
    /// <param name="currentDevMode">現在保持されているDEVMODEバイナリデータ（存在する場合）</param>
    /// <returns>OKが押されて設定が変更された場合は結果、キャンセルの場合は null</returns>
    PrinterSettingsDialogResult? ShowPrinterSettingsDialog(
        string printerName,
        nint ownerHwnd,
        byte[]? currentDevMode = null);

    /// <summary>
    /// システムにインストールされている利用可能なプリンター名の一覧を取得します。
    /// </summary>
    IReadOnlyList<string> GetInstalledPrinters();

    /// <summary>
    /// システムの既定（通常使う）プリンター名を取得します。
    /// </summary>
    string? GetDefaultPrinterName();

    /// <summary>
    /// 指定された印刷設定と面付けシート情報に基づいて印刷を実行します。
    /// </summary>
    /// <param name="renderPageFunc">ページインデックスからビットマップ画像を生成する関数</param>
    /// <param name="settings">印刷設定</param>
    /// <param name="sheets">面付けシート一覧</param>
    /// <param name="progress">進捗通知デリゲート（処理済みシート数, 総シート数）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>印刷が正常に完了したかどうか</returns>
    Task<bool> PrintAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        PrintSettings settings,
        IReadOnlyList<PrintSheetLayout> sheets,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default);
}
