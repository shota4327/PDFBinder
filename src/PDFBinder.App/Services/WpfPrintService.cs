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
    public async Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        IReadOnlyList<PrintSheetLayout> sheets,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sheets.Count == 0) return Array.Empty<PrintPreparedSheet>();

        var preparedSheets = new List<PrintPreparedSheet>(sheets.Count);
        var pageBitmapCache = new Dictionary<int, BitmapSource?>();

        for (int i = 0; i < sheets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = sheets[i];
            var placements = new List<PrintPreparedPlacement>(sheet.Placements.Count);

            foreach (var placement in sheet.Placements)
            {
                if (placement.PageIndex.HasValue)
                {
                    int pageIdx = placement.PageIndex.Value;
                    if (!pageBitmapCache.TryGetValue(pageIdx, out var bitmap))
                    {
                        bitmap = await renderPageFunc(pageIdx, cancellationToken);
                        pageBitmapCache[pageIdx] = bitmap;
                    }

                    placements.Add(new PrintPreparedPlacement(bitmap, placement.NormalizedBounds));
                }
            }

            preparedSheets.Add(new PrintPreparedSheet(placements, sheet.Orientation, sheet.PaperSize));
            progress?.Report((i + 1, sheets.Count));
        }

        return preparedSheets;
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
}
