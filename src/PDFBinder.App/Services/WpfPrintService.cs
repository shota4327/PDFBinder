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
    public async Task<bool> PrintAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        PrintSettings settings,
        IReadOnlyList<PrintSheetLayout> sheets,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sheets.Count == 0) return false;

        cancellationToken.ThrowIfCancellationRequested();

        // 用紙サイズ（ポイント単位: 72 DPI）を算出 (A4: 595.28 x 841.89, A3: 841.89 x 1190.55)
        var (pageWidth, pageHeight) = GetPaperDimensionsInPoints(settings.PaperSize, settings.Orientation);

        // FixedDocument をバックグラウンド／UI連携で構築
        var fixedDoc = new FixedDocument();
        fixedDoc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        for (int i = 0; i < sheets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = sheets[i];

            var fixedPage = await CreateFixedPageAsync(sheet, renderPageFunc, pageWidth, pageHeight, cancellationToken);
            var pageContent = new PageContent();
            ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
            fixedDoc.Pages.Add(pageContent);

            progress?.Report((i + 1, sheets.Count));
        }

        return ExecutePrintDialog(fixedDoc, settings);
    }

    /// <summary>
    /// 用紙サイズおよび向きからポイント単位（1/72インチ）の幅と高さを取得します。
    /// </summary>
    private static (double width, double height) GetPaperDimensionsInPoints(PrintPaperSize size, PrintOrientation orientation)
    {
        // A4: 210mm x 297mm (約 595.3 x 841.9 pt)
        // A3: 297mm x 420mm (約 841.9 x 1190.6 pt)
        double shortSide = size == PrintPaperSize.A3 ? 841.89 : 595.28;
        double longSide = size == PrintPaperSize.A3 ? 1190.55 : 841.89;

        return orientation == PrintOrientation.Landscape
            ? (longSide, shortSide)
            : (shortSide, longSide);
    }

    /// <summary>
    /// 1枚の用紙（シート）に対応する FixedPage 要素を非同期に作成します。
    /// </summary>
    private static async Task<FixedPage> CreateFixedPageAsync(
        PrintSheetLayout sheet,
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        double pageWidth,
        double pageHeight,
        CancellationToken cancellationToken)
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
            if (placement.PageIndex.HasValue)
            {
                var bitmap = await renderPageFunc(placement.PageIndex.Value, cancellationToken);
                if (bitmap != null)
                {
                    var image = CreatePlacedImage(bitmap, placement.NormalizedBounds, pageWidth, pageHeight);
                    canvas.Children.Add(image);
                }
            }
        }

        fixedPage.Children.Add(canvas);
        fixedPage.Measure(new Size(pageWidth, pageHeight));
        fixedPage.Arrange(new Rect(new Size(pageWidth, pageHeight)));
        fixedPage.UpdateLayout();

        return fixedPage;
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
    /// PrintDialog を構成して印刷ジョブを実行します。
    /// </summary>
    private static bool ExecutePrintDialog(FixedDocument fixedDoc, PrintSettings settings)
    {
        try
        {
            var printDialog = new PrintDialog();

            if (!string.IsNullOrEmpty(settings.PrinterName))
            {
                printDialog.PrintQueue = new PrintQueue(new LocalPrintServer(), settings.PrinterName);
            }

            ConfigurePrintTicket(printDialog, settings);
            printDialog.PrintDocument(fixedDoc.DocumentPaginator, "PDFBinder 印刷ジョブ");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 印刷設定に合わせて PrintTicket の用紙、向き、部数、両面設定を構成します。
    /// </summary>
    private static void ConfigurePrintTicket(PrintDialog printDialog, PrintSettings settings)
    {
        var ticket = printDialog.PrintTicket ?? printDialog.PrintQueue.DefaultPrintTicket.Clone();

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
