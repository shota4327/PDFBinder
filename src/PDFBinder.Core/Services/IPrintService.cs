using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// プリンター操作およびドキュメント印刷を行うインターフェース
/// </summary>
public interface IPrintService
{
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
