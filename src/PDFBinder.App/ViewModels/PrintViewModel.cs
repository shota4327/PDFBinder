using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.ViewModels;

/// <summary>
/// 印刷設定ダイアログの ViewModel
/// </summary>
public partial class PrintViewModel : ObservableObject
{
    private readonly IPrintService _printService;
    private readonly Func<int, int, int, CancellationToken, Task<BitmapSource?>> _renderPreviewPageFunc;
    private readonly Func<int, CancellationToken, Task<BitmapSource?>> _renderPrintPageFunc;
    private readonly int _totalPages;
    private readonly int _currentPageIndex;
    private CancellationTokenSource? _previewCts;

    [ObservableProperty]
    private PrintSettings _settings;

    [ObservableProperty]
    private ObservableCollection<string> _printers = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySheetPageText))]
    private int _currentSheetIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySheetPageText))]
    private int _totalSheets;

    /// <summary>プレビュー下部に表示するシート番号テキスト（例: "1 / 5"）</summary>
    public string DisplaySheetPageText => TotalSheets > 0 ? $"{CurrentSheetIndex + 1} / {TotalSheets}" : "0 / 0";

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private bool _isLoadingPreview;

    [ObservableProperty]
    private string? _rangeErrorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenPrinterSettings))]
    private bool _isPrinting;

    [ObservableProperty]
    private string _printProgressText = string.Empty;

    [ObservableProperty]
    private double _printProgressValue;

    private List<PrintSheetLayout> _currentSheets = new();
    private string _lastPrinterName = string.Empty;

    /// <summary>集約数（N-up）の選択項目</summary>
    public record NUpOptionItem(NUpPagesPerSheet Count, string DisplayName);

    /// <summary>選択可能な集約数の一覧</summary>
    public IReadOnlyList<NUpOptionItem> AvailableNUpOptions { get; } = new[]
    {
        new NUpOptionItem(NUpPagesPerSheet.Two, "2 ページ"),
        new NUpOptionItem(NUpPagesPerSheet.Four, "4 ページ"),
        new NUpOptionItem(NUpPagesPerSheet.Eight, "8 ページ")
    };

    /// <summary>印刷完了またはキャンセル時のコールバック</summary>
    public event Action<bool>? RequestClose;

    /// <summary>エラーが無く印刷実行が可能かどうか</summary>
    public bool CanPrint => !IsPrinting && string.IsNullOrEmpty(RangeErrorMessage) && TotalSheets > 0;

    /// <summary>プリンターの印刷設定を開くことができるかどうか</summary>
    public bool CanOpenPrinterSettings => !IsPrinting && !string.IsNullOrWhiteSpace(Settings.PrinterName);

    /// <summary>プレビュー表示用の用紙サイズ（縦横比）</summary>
    public double PreviewAspectWidth => Settings.Orientation == PrintOrientation.Landscape ? 1.414 : 1.0;
    public double PreviewAspectHeight => Settings.Orientation == PrintOrientation.Landscape ? 1.0 : 1.414;

    public PrintViewModel(
        IPrintService printService,
        PrintSettings settings,
        int totalPages,
        int currentPageIndex,
        Func<int, int, int, CancellationToken, Task<BitmapSource?>> renderPreviewPageFunc,
        Func<int, CancellationToken, Task<BitmapSource?>> renderPrintPageFunc)
    {
        _printService = printService;
        _settings = settings;
        _totalPages = totalPages;
        _currentPageIndex = currentPageIndex;
        _renderPreviewPageFunc = renderPreviewPageFunc;
        _renderPrintPageFunc = renderPrintPageFunc;

        InitializePrinters();
        _settings.PropertyChanged += OnSettingsPropertyChanged;
        UpdateSheetsAndPreview();
    }

    /// <summary>
    /// 利用可能なプリンター一覧を読み込み、初期プリンターを選択します。
    /// </summary>
    private void InitializePrinters()
    {
        var installed = _printService.GetInstalledPrinters();
        Printers.Clear();
        foreach (var p in installed)
        {
            Printers.Add(p);
        }

        if (string.IsNullOrEmpty(Settings.PrinterName) || !Printers.Contains(Settings.PrinterName))
        {
            var defaultPrinter = _printService.GetDefaultPrinterName();
            Settings.PrinterName = !string.IsNullOrEmpty(defaultPrinter) && Printers.Contains(defaultPrinter)
                ? defaultPrinter
                : Printers.FirstOrDefault() ?? string.Empty;
        }

        _lastPrinterName = Settings.PrinterName;
    }

    /// <summary>
    /// 印刷設定のプロパティ変更を検知して検証・面付け更新を行います。
    /// </summary>
    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrintSettings.PrinterName))
        {
            if (_lastPrinterName != Settings.PrinterName)
            {
                Settings.DriverDevMode = null;
                _lastPrinterName = Settings.PrinterName;
            }
            OpenPrinterSettingsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanOpenPrinterSettings));
        }

        if (e.PropertyName == nameof(PrintSettings.LayoutMode) && Settings.LayoutMode == PrintLayoutMode.Booklet)
        {
            Settings.Orientation = PrintOrientation.Landscape;
            Settings.DuplexMode = PrintDuplexMode.TwoSidedShortEdge;
        }

        ValidateAndRefresh();
    }

    /// <summary>
    /// 設定の入力検証を行い、面付けおよびプレビューを更新します。
    /// </summary>
    public void ValidateAndRefresh()
    {
        RangeErrorMessage = null;
        if (Settings.RangeType == PrintRangeType.Custom)
        {
            if (!PrintLayoutCalculator.TryParsePageRange(Settings.CustomRangeText, _totalPages, out _, out var error))
            {
                RangeErrorMessage = error ?? "無効なページ指定です。";
            }
        }

        OnPropertyChanged(nameof(CanPrint));
        OnPropertyChanged(nameof(PreviewAspectWidth));
        OnPropertyChanged(nameof(PreviewAspectHeight));

        if (string.IsNullOrEmpty(RangeErrorMessage))
        {
            UpdateSheetsAndPreview();
        }
        else
        {
            _currentSheets.Clear();
            TotalSheets = 0;
            CurrentSheetIndex = 0;
            PreviewImage = null;
            OnPropertyChanged(nameof(CanPrint));
        }
    }

    /// <summary>
    /// 面付けを再計算し、現在のシートのプレビュー画像を非同期生成します。
    /// </summary>
    private void UpdateSheetsAndPreview()
    {
        _currentSheets = PrintLayoutCalculator.CalculateSheets(Settings, _totalPages, _currentPageIndex);
        TotalSheets = _currentSheets.Count;

        if (CurrentSheetIndex >= TotalSheets)
        {
            CurrentSheetIndex = Math.Max(0, TotalSheets - 1);
        }

        OnPropertyChanged(nameof(CanPrint));
        _ = RefreshPreviewAsync();
    }

    /// <summary>
    /// 現在のシートのプレビュー画像を非同期に描画します。
    /// </summary>
    private async Task RefreshPreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        if (_currentSheets.Count == 0 || CurrentSheetIndex < 0 || CurrentSheetIndex >= _currentSheets.Count)
        {
            PreviewImage = null;
            return;
        }

        var sheet = _currentSheets[CurrentSheetIndex];
        IsLoadingPreview = true;

        try
        {
            var rendered = await RenderSheetPreviewAsync(sheet, ct);
            if (!ct.IsCancellationRequested)
            {
                PreviewImage = rendered;
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoadingPreview = false;
            }
        }
    }

    /// <summary>
    /// シート情報からプレビュー用のビットマップを合成します。
    /// </summary>
    private async Task<BitmapSource?> RenderSheetPreviewAsync(PrintSheetLayout sheet, CancellationToken ct)
    {
        int previewW = sheet.Orientation == PrintOrientation.Landscape ? 842 : 595;
        int previewH = sheet.Orientation == PrintOrientation.Landscape ? 595 : 842;

        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            // 用紙背景（白）
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, previewW, previewH));

            foreach (var placement in sheet.Placements)
            {
                if (placement.PageIndex.HasValue)
                {
                    double slotX = placement.NormalizedBounds.X * previewW;
                    double slotY = placement.NormalizedBounds.Y * previewH;
                    double slotW = placement.NormalizedBounds.Width * previewW;
                    double slotH = placement.NormalizedBounds.Height * previewH;

                    var pageBmp = await _renderPreviewPageFunc(placement.PageIndex.Value, (int)slotW, (int)slotH, ct);
                    if (pageBmp != null)
                    {
                        var rect = CalculateAspectFitRect(pageBmp.PixelWidth, pageBmp.PixelHeight, slotX, slotY, slotW, slotH);
                        dc.DrawImage(pageBmp, rect);
                    }
                }
            }
        }

        var rtb = new RenderTargetBitmap(previewW, previewH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(drawingVisual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// アスペクト比を維持してスロット矩形内に収まる描画領域を計算します。
    /// </summary>
    private static Rect CalculateAspectFitRect(double imgW, double imgH, double slotX, double slotY, double slotW, double slotH)
    {
        if (imgW <= 0 || imgH <= 0) return new Rect(slotX, slotY, slotW, slotH);
        double scale = Math.Min(slotW / imgW, slotH / imgH);
        double drawW = imgW * scale;
        double drawH = imgH * scale;
        double offsetX = slotX + (slotW - drawW) / 2.0;
        double offsetY = slotY + (slotH - drawH) / 2.0;
        return new Rect(offsetX, offsetY, drawW, drawH);
    }

    [RelayCommand]
    private void PreviousSheet()
    {
        if (CurrentSheetIndex > 0)
        {
            CurrentSheetIndex--;
            _ = RefreshPreviewAsync();
        }
    }

    [RelayCommand]
    private void NextSheet()
    {
        if (CurrentSheetIndex < TotalSheets - 1)
        {
            CurrentSheetIndex++;
            _ = RefreshPreviewAsync();
        }
    }

    [RelayCommand]
    private async Task ExecutePrintAsync()
    {
        if (!CanPrint) return;

        IsPrinting = true;
        OnPropertyChanged(nameof(CanPrint));
        PrintProgressText = $"印刷準備中... (0 / {TotalSheets})";
        PrintProgressValue = 0;

        var progress = new Progress<(int current, int total)>(p =>
        {
            PrintProgressText = $"印刷中... ({p.current} / {p.total})";
            PrintProgressValue = (double)p.current / p.total * 100.0;
        });

        try
        {
            bool success = await _printService.PrintAsync(
                _renderPrintPageFunc,
                Settings,
                _currentSheets,
                progress);

            RequestClose?.Invoke(success);
        }
        catch
        {
            RequestClose?.Invoke(false);
        }
        finally
        {
            IsPrinting = false;
            OnPropertyChanged(nameof(CanPrint));
        }
    }

    [RelayCommand]
    private void IncreaseCopies()
    {
        if (Settings.Copies < 99)
        {
            Settings.Copies++;
        }
    }

    [RelayCommand]
    private void DecreaseCopies()
    {
        if (Settings.Copies > 1)
        {
            Settings.Copies--;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenPrinterSettings))]
    private void OpenPrinterSettings()
    {
        if (!CanOpenPrinterSettings) return;

        nint ownerHwnd = nint.Zero;
        if (Application.Current?.MainWindow != null)
        {
            ownerHwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        }

        var result = _printService.ShowPrinterSettingsDialog(Settings.PrinterName, ownerHwnd, Settings.DriverDevMode);
        if (result != null)
        {
            ApplyPrinterSettingsResult(result);
        }
    }

    /// <summary>
    /// プリンター印刷設定ダイアログの結果をモデルに反映します。
    /// </summary>
    private void ApplyPrinterSettingsResult(PrinterSettingsDialogResult result)
    {
        Settings.DriverDevMode = result.DevModeData;
        if (result.PaperSize.HasValue)
        {
            Settings.PaperSize = result.PaperSize.Value;
        }
        if (result.Orientation.HasValue)
        {
            Settings.Orientation = result.Orientation.Value;
        }
        if (result.Copies.HasValue)
        {
            Settings.Copies = Math.Clamp(result.Copies.Value, 1, 99);
        }
        if (result.DuplexMode.HasValue)
        {
            Settings.DuplexMode = result.DuplexMode.Value;
        }

        ValidateAndRefresh();
    }

    [RelayCommand]
    private void Cancel()
    {
        _previewCts?.Cancel();
        RequestClose?.Invoke(false);
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Cleanup()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
    }
}
