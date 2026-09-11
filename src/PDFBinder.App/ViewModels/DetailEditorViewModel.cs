using System.Collections.ObjectModel;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDFBinder.App.Controls;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.ViewModels;

/// <summary>
/// ページ詳細手書きエディタのViewModel
/// </summary>
public partial class DetailEditorViewModel : ObservableObject, IDisposable
{
    private readonly IPdfRenderer _pdfRenderer;
    private readonly Action _onBackToGrid;
    private readonly Func<int, PdfPageModel?> _pageLookup;
    private readonly Stack<StrokeCollection> _strokeUndoStack = new();
    private readonly Stack<StrokeCollection> _strokeRedoStack = new();

    private CancellationTokenSource? _renderCts;
    private long _renderGeneration;
    private bool _isDisposed;

    /// <summary>
    /// ズーム操作後の動的レンダリング遅延（デバウンス）時間（ミリ秒）
    /// </summary>
    public int DebounceDelayMs { get; set; } = 150;

    /// <summary>
    /// ポイント単位（72pt/inch）をWPF論理ピクセル（96DIP/inch）に変換する標準基準スケール係数（約1.333）
    /// </summary>
    public const double PtToDipScale = 96.0 / 72.0;

    /// <summary>
    /// （後方互換用）詳細エディタ表示用の最大高解像度スケール係数
    /// </summary>
    public const double EditorRenderScale = 3.0;

    /// <summary>レンダリング最小ピクセル寸法（極端な縮小時の下限保護）</summary>
    public const int MinRenderDimension = 200;

    /// <summary>レンダリング最大ピクセル寸法（過大メモリ確保防止の上限保護）</summary>
    public const int MaxRenderDimension = 4096;

    [ObservableProperty]
    private PdfPageModel _currentPage;

    [ObservableProperty]
    private BitmapSource? _pageBackground;

    [ObservableProperty]
    private EditorToolMode _selectedTool = EditorToolMode.Pen;

    [ObservableProperty]
    private Color _selectedColor = Colors.Black;

    [ObservableProperty]
    private double _strokeThickness = 2.0;

    [ObservableProperty]
    private bool _isStraightLine;

    /// <summary>
    /// 直線トグルボタンを有効化できるか（ペンまたは蛍光ペン選択時のみtrue）
    /// </summary>
    public bool CanToggleStraightLine => SelectedTool == EditorToolMode.Pen || SelectedTool == EditorToolMode.Highlighter;

    /// <summary>最小ズーム倍率</summary>
    public const double MinZoom = 0.5;

    /// <summary>最大ズーム倍率</summary>
    public const double MaxZoom = 3.0;

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private bool _canUndoStroke;

    [ObservableProperty]
    private bool _canRedoStroke;

    /// <summary>黄色のプリセット・蛍光ペンデフォルト色（Tailwind Yellow 500: #EAB308）</summary>
    public static readonly Color YellowPresetColor = Color.FromRgb(0xEA, 0xB3, 0x08);

    /// <summary>カラーパレットプリセット（黒・赤・青・緑・黄）</summary>
    public ObservableCollection<Color> ColorPalette { get; } = new()
    {
        Color.FromRgb(0x00, 0x00, 0x00), // 黒
        Color.FromRgb(0xEF, 0x44, 0x44), // 赤
        Color.FromRgb(0x25, 0x63, 0xEB), // 青
        Color.FromRgb(0x16, 0xA3, 0x4A), // 緑
        YellowPresetColor                // 黄
    };

    public bool HasPreviousPage => _pageLookup(CurrentPage.PageNumber - 2) != null;
    public bool HasNextPage => _pageLookup(CurrentPage.PageNumber) != null;

    public DetailEditorViewModel(
        PdfPageModel initialPage,
        IPdfRenderer pdfRenderer,
        Action onBackToGrid,
        Func<int, PdfPageModel?> pageLookup)
    {
        _currentPage = initialPage;
        _pdfRenderer = pdfRenderer;
        _onBackToGrid = onBackToGrid;
        _pageLookup = pageLookup;

        _ = LoadPageBackgroundAsync();
    }

    partial void OnZoomChanged(double value)
    {
        _ = ScheduleDynamicRender(immediate: false);
    }

    /// <summary>
    /// 現在のズーム倍率およびページ寸法から最適なレンダリングピクセル寸法を算出します。
    /// </summary>
    public (int width, int height) CalculateRenderDimensions(double zoom)
    {
        double scale = PtToDipScale * zoom;
        int targetWidth = Math.Clamp((int)Math.Round(CurrentPage.DisplayWidth * scale), MinRenderDimension, MaxRenderDimension);
        int targetHeight = Math.Clamp((int)Math.Round(CurrentPage.DisplayHeight * scale), MinRenderDimension, MaxRenderDimension);
        return (targetWidth, targetHeight);
    }

    /// <summary>
    /// ズームまたはページ変更に応じた動的レンダリングをスケジュールします。
    /// </summary>
    public Task ScheduleDynamicRender(bool immediate = false)
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();

        var token = _renderCts.Token;
        long generation = Interlocked.Increment(ref _renderGeneration);

        return PerformDynamicRenderAsync(generation, token, immediate);
    }

    private async Task PerformDynamicRenderAsync(long generation, CancellationToken token, bool immediate)
    {
        try
        {
            if (!immediate && DebounceDelayMs > 0)
            {
                await Task.Delay(DebounceDelayMs, token);
            }

            token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _renderGeneration)) return;

            var (targetWidth, targetHeight) = CalculateRenderDimensions(Zoom);
            var rendered = await _pdfRenderer.RenderPageAsync(
                CurrentPage.SourceFilePath,
                CurrentPage.OriginalPageIndex,
                targetWidth,
                targetHeight,
                CurrentPage.RenderRotation,
                token);

            token.ThrowIfCancellationRequested();
            if (generation == Volatile.Read(ref _renderGeneration) && rendered != null)
            {
                // ダブルバッファリング: 新画像が完全に完成した瞬間のみ差し替え、チラつき（白飛び）を完全防止
                PageBackground = rendered;
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は正常終了
        }
        catch
        {
            // レンダリング例外時もクラッシュを防止
        }
    }

    /// <summary>
    /// ページの背景ビットマップを現在のズーム倍率に合わせて即座にレンダリングします。
    /// </summary>
    public async Task LoadPageBackgroundAsync()
    {
        await ScheduleDynamicRender(immediate: true);
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
    }

    partial void OnSelectedToolChanged(EditorToolMode value)
    {
        // ツール切り替え時は直線トグルを自動的にオフへリセット
        IsStraightLine = false;
        OnPropertyChanged(nameof(CanToggleStraightLine));

        if (value == EditorToolMode.Highlighter)
        {
            if (SelectedColor == Colors.Black)
            {
                SelectedColor = YellowPresetColor;
            }
            StrokeThickness = 12.0;
        }
        else if (value == EditorToolMode.Pen)
        {
            if (SelectedColor == YellowPresetColor)
            {
                SelectedColor = Colors.Black;
            }
            StrokeThickness = 2.0;
        }
    }

    [RelayCommand]
    private void SelectTool(EditorToolMode tool)
    {
        SelectedTool = tool;
    }

    [RelayCommand]
    private void SelectColor(Color color)
    {
        SelectedColor = color;
    }

    /// <summary>
    /// プリセットの太さを設定します。
    /// </summary>
    [RelayCommand]
    private void SetPresetThickness(object? parameter)
    {
        if (parameter == null) return;
        if (parameter is double d)
        {
            StrokeThickness = d;
        }
        else if (double.TryParse(parameter.ToString(), System.Globalization.CultureInfo.InvariantCulture, out double parsed))
        {
            StrokeThickness = parsed;
        }
    }

    /// <summary>
    /// ズーム倍率を直接設定します（境界値内にクランプ）。
    /// </summary>
    public void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(Math.Round(zoom, 3), MinZoom, MaxZoom);
    }

    [RelayCommand]
    private void ZoomIn()
    {
        if (Zoom < MaxZoom) Zoom = Math.Round(Math.Min(Zoom + 0.25, MaxZoom), 2);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (Zoom > MinZoom) Zoom = Math.Round(Math.Max(Zoom - 0.25, MinZoom), 2);
    }

    [RelayCommand]
    private void ZoomReset()
    {
        Zoom = 1.0;
    }

    [RelayCommand]
    private async Task GoToPreviousPageAsync()
    {
        var prev = _pageLookup(CurrentPage.PageNumber - 2);
        if (prev != null)
        {
            CurrentPage = prev;
            await LoadPageBackgroundAsync();
        }
    }

    [RelayCommand]
    private async Task GoToNextPageAsync()
    {
        var next = _pageLookup(CurrentPage.PageNumber);
        if (next != null)
        {
            CurrentPage = next;
            await LoadPageBackgroundAsync();
        }
    }

    [RelayCommand]
    private void BackToGrid()
    {
        _onBackToGrid();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// アンマネージリソースおよびマネージリソースを解放します。
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _renderCts?.Cancel();
                _renderCts?.Dispose();
                _renderCts = null;
            }
            _isDisposed = true;
        }
    }
}
