using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.Services;

/// <summary>
/// WPF System.Printing を使用した印刷サービスの実装クラス
/// </summary>
public class WpfPrintService : IPrintService
{
    /// <inheritdoc/>
    public IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            using var printServer = new LocalPrintServer();
            var queues = printServer.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            });

            return queues.Select(q => q.Name).OrderBy(n => n).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc/>
    public string? GetDefaultPrinterName()
    {
        try
        {
            using var printServer = new LocalPrintServer();
            return printServer.DefaultPrintQueue?.Name;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public PrinterSettingsDialogResult? ShowPrinterSettingsDialog(
        string printerName,
        nint ownerHwnd,
        byte[]? currentDevMode = null)
    {
        return PrinterDevModeHelper.ShowDocumentPropertiesDialog(printerName, ownerHwnd, currentDevMode);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        IReadOnlyList<PrintSheetLayout> sheets,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return PrepareSheetsAsync(
            (idx, w, h, ct) => renderPageFunc(idx, ct),
            sheets,
            null,
            progress,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
        Func<int, int, int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        IReadOnlyList<PrintSheetLayout> sheets,
        IReadOnlyList<(double width, double height)>? pageDimensions = null,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sheets.Count == 0) return Array.Empty<PrintPreparedSheet>();

        var preparedSheets = new List<PrintPreparedSheet>(sheets.Count);
        var pageBitmapCache = new Dictionary<(int pageIdx, int w, int h), BitmapSource?>();

        for (int i = 0; i < sheets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = sheets[i];
            var preparedSheet = await PrepareSingleSheetAsync(
                sheet,
                renderPageFunc,
                pageDimensions,
                pageBitmapCache,
                cancellationToken);

            preparedSheets.Add(preparedSheet);
            progress?.Report((i + 1, sheets.Count));
        }

        return preparedSheets;
    }

    /// <summary>
    /// 1枚のシートに含まれる配置スロットの画像を処理し、事前準備シートを生成します。
    /// </summary>
    private static async Task<PrintPreparedSheet> PrepareSingleSheetAsync(
        PrintSheetLayout sheet,
        Func<int, int, int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        IReadOnlyList<(double width, double height)>? pageDimensions,
        Dictionary<(int pageIdx, int w, int h), BitmapSource?> pageBitmapCache,
        CancellationToken ct)
    {
        var placements = new List<PrintPreparedPlacement>(sheet.Placements.Count);

        foreach (var placement in sheet.Placements)
        {
            if (placement.PageIndex.HasValue)
            {
                int pageIdx = placement.PageIndex.Value;
                var (targetW, targetH) = DetermineRenderDimensions(sheet, placement.NormalizedBounds, pageIdx, pageDimensions);

                var key = (pageIdx, targetW, targetH);
                if (!pageBitmapCache.TryGetValue(key, out var bitmap))
                {
                    var rawBitmap = await renderPageFunc(pageIdx, targetW, targetH, ct);
                    bitmap = rawBitmap != null ? ConvertToCompressedPngBitmap(rawBitmap) : null;
                    pageBitmapCache[key] = bitmap;
                }

                placements.Add(new PrintPreparedPlacement(bitmap, placement.NormalizedBounds));
            }
        }

        return new PrintPreparedSheet(placements, sheet.Orientation, sheet.PaperSize);
    }

    /// <summary>
    /// シート情報、スロット位置、ページ元寸法から300 DPI基準のレンダリング解像度（幅・高さ）を決定します。
    /// </summary>
    internal static (int width, int height) DetermineRenderDimensions(
        PrintSheetLayout sheet,
        Rect normalizedBounds,
        int pageIdx,
        IReadOnlyList<(double width, double height)>? pageDimensions)
    {
        var (slotW, slotH) = CalculateSlotPixelDimensions(sheet.PaperSize, sheet.Orientation, normalizedBounds);

        if (pageDimensions != null && pageIdx >= 0 && pageIdx < pageDimensions.Count)
        {
            var (origW, origH) = pageDimensions[pageIdx];
            return CalculatePageRenderDimensions(slotW, slotH, origW, origH);
        }

        return (slotW, slotH);
    }

    /// <inheritdoc/>
    public Task<bool> SpoolDocumentAsync(
        IReadOnlyList<PrintPreparedSheet> preparedSheets,
        PrintSettings settings,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        if (preparedSheets.Count == 0) return Task.FromResult(false);

        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<bool>();

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                bool success = ExecutePrintSpool(preparedSheets, settings, jobName);
                tcs.TrySetResult(success);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    /// <inheritdoc/>
    public async Task<bool> PrintAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        PrintSettings settings,
        IReadOnlyList<PrintSheetLayout> sheets,
        string jobName = "PDFBinder",
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var preparedSheets = await PrepareSheetsAsync(renderPageFunc, sheets, progress, cancellationToken);
        return await SpoolDocumentAsync(preparedSheets, settings, jobName, cancellationToken);
    }

    /// <summary>
    /// 用紙サイズおよび向きから WPF 座標系論理ピクセル単位（96 DPI、1/96インチ）の幅と高さを取得します。
    /// </summary>
    internal static (double width, double height) GetPaperDimensionsInDips(PrintPaperSize size, PrintOrientation orientation)
    {
        // A4: 210mm x 297mm (210 / 25.4 * 96 ≈ 793.70, 297 / 25.4 * 96 ≈ 1122.52 px)
        // A3: 297mm x 420mm (297 / 25.4 * 96 ≈ 1122.52, 420 / 25.4 * 96 ≈ 1587.40 px)
        double shortSide = size == PrintPaperSize.A3 ? 1122.52 : 793.70;
        double longSide = size == PrintPaperSize.A3 ? 1587.40 : 1122.52;

        return orientation == PrintOrientation.Landscape
            ? (longSide, shortSide)
            : (shortSide, longSide);
    }

    /// <summary>
    /// 正規化座標および用紙サイズから配置用 Image コントロールを生成します。
    /// </summary>
    private static Image CreatePlacedImage(BitmapSource bitmap, Rect normalizedBounds, double pageWidth, double pageHeight)
    {
        double slotX = normalizedBounds.X * pageWidth;
        double slotY = normalizedBounds.Y * pageHeight;
        double slotWidth = normalizedBounds.Width * pageWidth;
        double slotHeight = normalizedBounds.Height * pageHeight;

        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            Width = slotWidth,
            Height = slotHeight
        };

        Canvas.SetLeft(image, slotX);
        Canvas.SetTop(image, slotY);
        return image;
    }

    /// <summary>
    /// 事前レンダリング済みシートから FixedDocument を構築し、印刷スプールを実行します。
    /// </summary>
    private static bool ExecutePrintSpool(
        IReadOnlyList<PrintPreparedSheet> preparedSheets,
        PrintSettings settings,
        string jobName)
    {
        var (pageWidth, pageHeight) = GetPaperDimensionsInDips(settings.PaperSize, settings.Orientation);

        var fixedDoc = new FixedDocument();
        fixedDoc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        foreach (var sheet in preparedSheets)
        {
            var fixedPage = CreateFixedPageFromPrepared(sheet, pageWidth, pageHeight);
            var pageContent = new PageContent();
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
            fixedDoc.Pages.Add(pageContent);
        }

        return ExecutePrintDialog(fixedDoc, settings, jobName);
    }

    /// <summary>
    /// 1枚の事前準備シートに対応する FixedPage 要素を作成します。
    /// </summary>
    private static FixedPage CreateFixedPageFromPrepared(PrintPreparedSheet sheet, double pageWidth, double pageHeight)
    {
        var fixedPage = new FixedPage
        {
            Width = pageWidth,
            Height = pageHeight,
            Background = Brushes.White
        };

        var canvas = new Canvas
        {
            Width = pageWidth,
            Height = pageHeight
        };

        foreach (var placement in sheet.Placements)
        {
            if (placement.Image != null)
            {
                var image = CreatePlacedImage(placement.Image, placement.NormalizedBounds, pageWidth, pageHeight);
                canvas.Children.Add(image);
            }
        }

        fixedPage.Children.Add(canvas);
        fixedPage.Measure(new Size(pageWidth, pageHeight));
        fixedPage.Arrange(new Rect(new Size(pageWidth, pageHeight)));
        fixedPage.UpdateLayout();

        return fixedPage;
    }

    /// <summary>
    /// PrintDialog を構成して印刷ジョブを実行します。
    /// </summary>
    private static bool ExecutePrintDialog(FixedDocument fixedDoc, PrintSettings settings, string jobName)
    {
        var printDialog = new PrintDialog();

        if (!string.IsNullOrEmpty(settings.PrinterName))
        {
            printDialog.PrintQueue = new PrintQueue(new LocalPrintServer(), settings.PrinterName);
        }

        ConfigurePrintTicket(printDialog, settings);
        printDialog.PrintDocument(fixedDoc.DocumentPaginator, jobName);
        return true;
    }

    /// <summary>
    /// 印刷設定に合わせて PrintTicket の用紙、向き、部数、両面設定を構成します。
    /// </summary>
    private static void ConfigurePrintTicket(PrintDialog printDialog, PrintSettings settings)
    {
        PrintTicket? devModeTicket = null;
        if (settings.DriverDevMode != null && settings.DriverDevMode.Length > 0 && printDialog.PrintQueue != null)
        {
            devModeTicket = PrinterDevModeHelper.CreatePrintTicketFromDevMode(
                settings.PrinterName,
                settings.DriverDevMode,
                printDialog.PrintQueue);
        }

        var ticket = devModeTicket ?? printDialog.PrintTicket ?? printDialog.PrintQueue?.DefaultPrintTicket.Clone() ?? new PrintTicket();

        ticket.PageOrientation = settings.Orientation == PrintOrientation.Landscape
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;

        ticket.PageMediaSize = settings.PaperSize == PrintPaperSize.A3
            ? new PageMediaSize(PageMediaSizeName.ISOA3)
            : new PageMediaSize(PageMediaSizeName.ISOA4);

        if (settings.Copies > 1)
        {
            ticket.CopyCount = settings.Copies;
        }

        ticket.Duplexing = settings.DuplexMode switch
        {
            PrintDuplexMode.TwoSidedLongEdge => Duplexing.TwoSidedLongEdge,
            PrintDuplexMode.TwoSidedShortEdge => Duplexing.TwoSidedShortEdge,
            _ => Duplexing.OneSided
        };

        printDialog.PrintTicket = ticket;
    }

    /// <summary>
    /// 用紙サイズおよび向きから 300 DPI 基準のピクセル幅と高さを算出します。
    /// </summary>
    internal static (int width, int height) GetPaperPixelDimensions(PrintPaperSize size, PrintOrientation orientation, double dpi = 300.0)
    {
        double shortSideMm = size == PrintPaperSize.A3 ? 297.0 : 210.0;
        double longSideMm = size == PrintPaperSize.A3 ? 420.0 : 297.0;

        int shortPx = (int)Math.Round(shortSideMm / 25.4 * dpi);
        int longPx = (int)Math.Round(longSideMm / 25.4 * dpi);

        return orientation == PrintOrientation.Landscape
            ? (longPx, shortPx)
            : (shortPx, longPx);
    }

    /// <summary>
    /// 用紙サイズ、向き、正規化領域からスロットのピクセル寸法を算出します。
    /// </summary>
    internal static (int width, int height) CalculateSlotPixelDimensions(
        PrintPaperSize paperSize,
        PrintOrientation orientation,
        Rect normalizedBounds,
        double dpi = 300.0)
    {
        var (sheetW, sheetH) = GetPaperPixelDimensions(paperSize, orientation, dpi);
        int slotW = Math.Max(1, (int)Math.Round(normalizedBounds.Width * sheetW));
        int slotH = Math.Max(1, (int)Math.Round(normalizedBounds.Height * sheetH));
        return (slotW, slotH);
    }

    /// <summary>
    /// スロット寸法と元ページの縦横比から、アスペクト比を維持してスロットに内接するピクセル寸法を算出します。
    /// </summary>
    internal static (int width, int height) CalculatePageRenderDimensions(
        int slotW,
        int slotH,
        double origW,
        double origH)
    {
        if (origW <= 0 || origH <= 0 || slotW <= 0 || slotH <= 0)
        {
            return (Math.Max(1, slotW), Math.Max(1, slotH));
        }

        double scale = Math.Min((double)slotW / origW, (double)slotH / origH);
        int renderW = Math.Max(1, (int)Math.Round(origW * scale));
        int renderH = Math.Max(1, (int)Math.Round(origH * scale));
        return (renderW, renderH);
    }

    /// <summary>
    /// BitmapSource をメモリ上で可逆圧縮 PNG の BitmapImage に変換します。
    /// これにより保持メモリ量を削減し、WPF XPS スプーラでの再エンコードを回避して送信を高速化します。
    /// </summary>
    internal static BitmapSource ConvertToCompressedPngBitmap(BitmapSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = ms;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }
}
