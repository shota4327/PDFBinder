using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.Services;

/// <summary>
/// ページの印刷描画方式の分類
/// </summary>
internal enum PagePrintRenderKind
{
    /// <summary>白紙ページ（インクなし）</summary>
    Blank,

    /// <summary>PDFium による GDI ベクター直接描画</summary>
    PdfVector,

    /// <summary>24bpp 透過なし GDI ラスター描画</summary>
    Raster
}

/// <summary>
/// Win32 GDI + PDFium ハイブリッド印刷を提供する <see cref="IPrintService"/> 実装
/// </summary>
public class GdiPrintService : IPrintService
{
    private readonly IPdfRenderer _pdfRenderer;
    private readonly WpfPrintService _legacyPrintService = new();

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public GdiPrintService(IPdfRenderer? pdfRenderer = null)
    {
        _pdfRenderer = pdfRenderer ?? new PdfiumRenderer();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetInstalledPrinters() => _legacyPrintService.GetInstalledPrinters();

    /// <inheritdoc/>
    public string? GetDefaultPrinterName() => _legacyPrintService.GetDefaultPrinterName();

    /// <inheritdoc/>
    public PrinterSettingsDialogResult? ShowPrinterSettingsDialog(
        string printerName,
        nint ownerHwnd,
        byte[]? currentDevMode = null)
        => _legacyPrintService.ShowPrinterSettingsDialog(printerName, ownerHwnd, currentDevMode);

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        IReadOnlyList<PrintSheetLayout> sheets,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
        => _legacyPrintService.PrepareSheetsAsync(renderPageFunc, sheets, progress, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrintPreparedSheet>> PrepareSheetsAsync(
        Func<int, int, int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        IReadOnlyList<PrintSheetLayout> sheets,
        IReadOnlyList<(double width, double height)>? pageDimensions = null,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
        => _legacyPrintService.PrepareSheetsAsync(renderPageFunc, sheets, pageDimensions, progress, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> SpoolDocumentAsync(
        IReadOnlyList<PrintPreparedSheet> preparedSheets,
        PrintSettings settings,
        string jobName,
        CancellationToken cancellationToken = default)
        => _legacyPrintService.SpoolDocumentAsync(preparedSheets, settings, jobName, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> PrintAsync(
        Func<int, CancellationToken, Task<BitmapSource?>> renderPageFunc,
        PrintSettings settings,
        IReadOnlyList<PrintSheetLayout> sheets,
        string jobName = "PDFBinder",
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
        => _legacyPrintService.PrintAsync(renderPageFunc, settings, sheets, jobName, progress, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> PrintStreamingAsync(
        IReadOnlyList<PrintSheetLayout> sheets,
        IReadOnlyList<PrintPageSource> pageSources,
        PrintSettings settings,
        string jobName,
        IProgress<(int currentSheet, int totalSheets)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (sheets == null || sheets.Count == 0)
        {
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool result = ExecuteStreamingJob(sheets, pageSources, settings, jobName, progress, cancellationToken);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
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

    /// <summary>
    /// ページ種別と印刷設定から、使用すべき描画方式（白紙・ベクター・ラスター）を判定します。
    /// </summary>
    internal static PagePrintRenderKind DeterminePageRenderKind(PrintPageSource? page, PrintSettings settings)
    {
        if (page == null || (page.IsBlankPage && !page.HasInk))
        {
            return PagePrintRenderKind.Blank;
        }

        if (settings.PrintAsImage || page.IsImage || page.HasInk || string.IsNullOrEmpty(page.SourceFilePath))
        {
            return PagePrintRenderKind.Raster;
        }

        return PagePrintRenderKind.PdfVector;
    }

    /// <summary>
    /// 指定された描画方式に従って処理を実行し、ベクター描画失敗時は自動でラスター描画へフォールバックします。
    /// </summary>
    internal static bool ExecutePlacementWithFallback(
        PagePrintRenderKind kind,
        Func<bool> vectorAction,
        Action rasterAction)
    {
        if (kind == PagePrintRenderKind.Blank)
        {
            return true;
        }

        if (kind == PagePrintRenderKind.PdfVector)
        {
            try
            {
                if (vectorAction())
                {
                    return true;
                }
            }
            catch
            {
                // ベクター描画例外時は自動でラスターフォールバックへ移行
            }
        }

        rasterAction();
        return true;
    }

    /// <summary>
    /// 印刷対象のプリンター名を解決し、DEVMODE とデバイスコンテキストを構築して全シートを順次送信します。
    /// </summary>
    private bool ExecuteStreamingJob(
        IReadOnlyList<PrintSheetLayout> sheets,
        IReadOnlyList<PrintPageSource> pageSources,
        PrintSettings settings,
        string jobName,
        IProgress<(int currentSheet, int totalSheets)>? progress,
        CancellationToken ct)
    {
        string? printerName = ResolveTargetPrinterName(settings);
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new InvalidOperationException("印刷先のプリンターが見つかりません。");
        }

        byte[]? devMode = PrinterDevModeHelper.BuildEffectiveDevMode(printerName, settings, out bool driverHandlesCopies);
        nint hdc = CreatePrinterDeviceContext(printerName, devMode);
        if (hdc == nint.Zero)
        {
            throw new InvalidOperationException($"プリンター「{printerName}」のデバイスコンテキストを作成できませんでした。");
        }

        var docCache = new PdfiumPrintDocumentCache();
        try
        {
            return RunPrintDocumentLoop(hdc, sheets, pageSources, settings, jobName, driverHandlesCopies, docCache, progress, ct);
        }
        finally
        {
            CloseDocumentCacheSafely(docCache);
            GdiNativeMethods.DeleteDC(hdc);
        }
    }

    /// <summary>
    /// 印刷先プリンター名を解決します（未指定時は既定プリンターを使用）。
    /// </summary>
    private string? ResolveTargetPrinterName(PrintSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PrinterName))
        {
            return settings.PrinterName;
        }

        return GetDefaultPrinterName();
    }

    /// <summary>
    /// DEVMODE を適用したプリンターデバイスコンテキストを作成します。
    /// </summary>
    private static nint CreatePrinterDeviceContext(string printerName, byte[]? devMode)
    {
        if (devMode == null || devMode.Length == 0)
        {
            return GdiNativeMethods.CreateDCW("WINSPOOL", printerName, null, nint.Zero);
        }

        GCHandle pinned = GCHandle.Alloc(devMode, GCHandleType.Pinned);
        try
        {
            return GdiNativeMethods.CreateDCW("WINSPOOL", printerName, null, pinned.AddrOfPinnedObject());
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>
    /// StartDocW〜EndDoc の間で部数および各シートを順次送信します。
    /// </summary>
    private bool RunPrintDocumentLoop(
        nint hdc,
        IReadOnlyList<PrintSheetLayout> sheets,
        IReadOnlyList<PrintPageSource> pageSources,
        PrintSettings settings,
        string jobName,
        bool driverHandlesCopies,
        PdfiumPrintDocumentCache docCache,
        IProgress<(int currentSheet, int totalSheets)>? progress,
        CancellationToken ct)
    {
        var docInfo = new GdiNativeMethods.DOCINFOW
        {
            cbSize = Marshal.SizeOf<GdiNativeMethods.DOCINFOW>(),
            lpszDocName = string.IsNullOrWhiteSpace(jobName) ? "PDFBinder" : jobName
        };

        if (GdiNativeMethods.StartDocW(hdc, ref docInfo) <= 0)
        {
            throw new InvalidOperationException("印刷ドキュメントの開始（StartDoc）に失敗しました。");
        }

        bool docEnded = false;
        try
        {
            var metrics = GdiBitmapConverter.ReadDevicePageMetrics(hdc);
            int copyLoops = driverHandlesCopies ? 1 : Math.Clamp(settings.Copies, 1, 99);
            int totalReports = sheets.Count * copyLoops;
            int reported = 0;

            for (int copy = 0; copy < copyLoops; copy++)
            {
                for (int i = 0; i < sheets.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    PrintSingleSheet(hdc, metrics, sheets[i], pageSources, settings, docCache, ct);
                    reported++;
                    progress?.Report((reported, totalReports));
                }
            }

            if (GdiNativeMethods.EndDoc(hdc) <= 0)
            {
                throw new InvalidOperationException("印刷ドキュメントの終了（EndDoc）に失敗しました。");
            }

            docEnded = true;
            return true;
        }
        finally
        {
            if (!docEnded)
            {
                GdiNativeMethods.AbortDoc(hdc);
            }
        }
    }

    /// <summary>
    /// 1枚の用紙（シート）に含まれるすべての配置スロットを GDI ページへ描画します。
    /// </summary>
    private void PrintSingleSheet(
        nint hdc,
        DevicePageMetrics metrics,
        PrintSheetLayout sheet,
        IReadOnlyList<PrintPageSource> pageSources,
        PrintSettings settings,
        PdfiumPrintDocumentCache docCache,
        CancellationToken ct)
    {
        if (GdiNativeMethods.StartPage(hdc) <= 0)
        {
            throw new InvalidOperationException($"印刷ページ（シート {sheet.SheetNumber}）の開始に失敗しました。");
        }

        bool pageEnded = false;
        try
        {
            foreach (var placement in sheet.Placements)
            {
                ct.ThrowIfCancellationRequested();
                if (!placement.PageIndex.HasValue) continue;

                int idx = placement.PageIndex.Value;
                if (idx < 0 || idx >= pageSources.Count) continue;

                var page = pageSources[idx];
                var slotRect = GdiBitmapConverter.CalculateSlotRect(metrics, placement.NormalizedBounds);
                PrintSlotWithClip(hdc, metrics, sheet, placement, slotRect, page, settings, docCache, ct);
            }

            if (GdiNativeMethods.EndPage(hdc) <= 0)
            {
                throw new InvalidOperationException($"印刷ページ（シート {sheet.SheetNumber}）の終了に失敗しました。");
            }

            pageEnded = true;
        }
        finally
        {
            if (!pageEnded)
            {
                GdiNativeMethods.EndPage(hdc);
            }
        }
    }

    /// <summary>
    /// SaveDC / IntersectClipRect / RestoreDC でスロット境界を保護しつつ1ページ分を描画します。
    /// </summary>
    private void PrintSlotWithClip(
        nint hdc,
        DevicePageMetrics metrics,
        PrintSheetLayout sheet,
        PrintPagePlacement placement,
        DeviceRect slotRect,
        PrintPageSource page,
        PrintSettings settings,
        PdfiumPrintDocumentCache docCache,
        CancellationToken ct)
    {
        int savedDc = GdiNativeMethods.SaveDC(hdc);
        try
        {
            GdiNativeMethods.IntersectClipRect(hdc, slotRect.Left, slotRect.Top, slotRect.Right, slotRect.Bottom);
            var destRect = GdiBitmapConverter.CalculateAspectFitRect(
                slotRect,
                page.DisplayWidth,
                page.DisplayHeight,
                metrics.DpiX,
                metrics.DpiY);

            var kind = DeterminePageRenderKind(page, settings);
            ExecutePlacementWithFallback(
                kind,
                () => TryRenderPdfVectorToDc(hdc, destRect, page, docCache, ct),
                () => RenderRasterToDc(hdc, destRect, sheet, placement.NormalizedBounds, page, ct));
        }
        finally
        {
            if (savedDc != 0)
            {
                GdiNativeMethods.RestoreDC(hdc, savedDc);
            }
        }
    }

    /// <summary>
    /// PDFium の FPDF_RenderPage を使用してプリンター HDC へ直接ベクター描画します。
    /// </summary>
    private bool TryRenderPdfVectorToDc(
        nint hdc,
        DeviceRect destRect,
        PrintPageSource page,
        PdfiumPrintDocumentCache docCache,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(page.SourceFilePath))
        {
            return false;
        }

        byte[]? cleanBytes = _pdfRenderer.GetCleanPdfBytesIfHasMarker(page.SourceFilePath, page.SourcePassword, ct);
        using var releaser = _pdfRenderer.AcquirePdfiumLockAsync(RenderPriority.Low, ct).GetAwaiter().GetResult();

        nint doc = docCache.GetOrLoadDocument(page.SourceFilePath, page.SourcePassword, cleanBytes);
        if (doc == nint.Zero)
        {
            return false;
        }

        nint pdfPage = PdfiumNativePrint.FPDF_LoadPage(doc, page.OriginalPageIndex);
        if (pdfPage == nint.Zero)
        {
            return false;
        }

        try
        {
            PdfiumNativePrint.FPDF_SetPrintMode(PdfiumNativePrint.FPDF_PRINTMODE_EMF);
            int rotate = ToPdfiumRotateCode(page.RenderRotation);
            PdfiumNativePrint.FPDF_RenderPage(
                hdc,
                pdfPage,
                destRect.Left,
                destRect.Top,
                destRect.Width,
                destRect.Height,
                rotate,
                PdfiumNativePrint.FPDF_ANNOT | PdfiumNativePrint.FPDF_PRINTING);
            return true;
        }
        finally
        {
            PdfiumNativePrint.FPDF_ClosePage(pdfPage);
        }
    }

    /// <summary>
    /// ページを 300 DPI の 24bpp 非透過 RGB ビットマップへ描画し、StretchDIBits で GDI へ送信します。
    /// </summary>
    private void RenderRasterToDc(
        nint hdc,
        DeviceRect destRect,
        PrintSheetLayout sheet,
        Rect normalizedBounds,
        PrintPageSource page,
        CancellationToken ct)
    {
        var (slotW, slotH) = WpfPrintService.CalculateSlotPixelDimensions(sheet.PaperSize, sheet.Orientation, normalizedBounds);
        var (targetW, targetH) = WpfPrintService.CalculatePageRenderDimensions(slotW, slotH, page.DisplayWidth, page.DisplayHeight);

        BitmapSource? bitmap = RenderPageWithInkForPrint(page, targetW, targetH, ct);
        if (bitmap == null) return;

        byte[] bgrBytes = GdiBitmapConverter.ConvertToBgr24TopDown(bitmap, out int srcW, out int srcH, out _);
        SendBgr24ToDeviceContext(hdc, destRect, bgrBytes, srcW, srcH);
    }

    /// <summary>
    /// ページ種別に応じてベース画像を描画し、手書きインクが存在する場合は合成します。
    /// </summary>
    private BitmapSource? RenderPageWithInkForPrint(PrintPageSource page, int targetW, int targetH, CancellationToken ct)
    {
        BitmapSource? baseBitmap = string.IsNullOrEmpty(page.SourceFilePath)
            ? _pdfRenderer.CreateBlankPageBitmap(targetW, targetH, PageRotation.Rotate0)
            : _pdfRenderer.RenderPageAsync(
                page.SourceFilePath,
                page.OriginalPageIndex,
                targetW,
                targetH,
                page.RenderRotation,
                ct,
                RenderPriority.Low,
                page.SourcePassword).GetAwaiter().GetResult();

        if (baseBitmap != null && page.HasInk)
        {
            return _pdfRenderer.CompositeStrokes(baseBitmap, page.InkStrokes, page.DisplayWidth, page.DisplayHeight);
        }

        return baseBitmap;
    }

    /// <summary>
    /// 24bpp トップダウン BGR データを StretchDIBits（HALFTONE 補間）によりデバイスコンテキストへ転送します。
    /// </summary>
    private static void SendBgr24ToDeviceContext(
        nint hdc,
        DeviceRect destRect,
        byte[] bgrBytes,
        int srcWidth,
        int srcHeight)
    {
        GdiNativeMethods.SetStretchBltMode(hdc, GdiNativeMethods.HALFTONE);
        GdiNativeMethods.SetBrushOrgEx(hdc, 0, 0, out _);

        var bmi = new GdiNativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<GdiNativeMethods.BITMAPINFOHEADER>(),
            biWidth = srcWidth,
            biHeight = -srcHeight, // トップダウン DIB
            biPlanes = 1,
            biBitCount = 24,
            biCompression = GdiNativeMethods.BI_RGB,
            biSizeImage = (uint)bgrBytes.Length
        };

        GdiNativeMethods.StretchDIBits(
            hdc,
            destRect.Left,
            destRect.Top,
            destRect.Width,
            destRect.Height,
            0,
            0,
            srcWidth,
            srcHeight,
            bgrBytes,
            ref bmi,
            GdiNativeMethods.DIB_RGB_COLORS,
            GdiNativeMethods.SRCCOPY);
    }

    /// <summary>
    /// ジョブ内 PDFium ドキュメントキャッシュを排他ロック下で安全に破棄します。
    /// </summary>
    private void CloseDocumentCacheSafely(PdfiumPrintDocumentCache docCache)
    {
        try
        {
            using var releaser = _pdfRenderer.AcquirePdfiumLockAsync(RenderPriority.Low, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            docCache.CloseAll();
        }
        catch
        {
            docCache.CloseAll();
        }
    }

    /// <summary>
    /// <see cref="PageRotation"/> を PDFium FPDF_RenderPage の rotate 引数（0〜3）に変換します。
    /// </summary>
    internal static int ToPdfiumRotateCode(PageRotation rotation)
    {
        return rotation switch
        {
            PageRotation.Rotate90 => 1,
            PageRotation.Rotate180 => 2,
            PageRotation.Rotate270 => 3,
            _ => 0
        };
    }
}
