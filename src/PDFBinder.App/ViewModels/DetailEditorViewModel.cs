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

    private Color _penColor = Colors.Black;
    private double _penThickness = 1.0;
    private Color _highlighterColor = YellowPresetColor;
    private double _highlighterThickness = 12.0;
    private double _eraserPointThickness = 12.0;

    /// <summary>
    /// 全ページの表示状態を管理するコレクション
    /// </summary>
    public ObservableCollection<DetailPageItemViewModel> Pages { get; } = new();

    /// <summary>
    /// 現在表示中・操作対象のページ
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageBackground))]
    [NotifyPropertyChangedFor(nameof(CurrentPageItem))]
    private PdfPageModel? _currentPage;

    /// <summary>
    /// 現在表示中ページの背景画像（後方互換用）
    /// </summary>
    public BitmapSource? PageBackground
    {
        get => CurrentPageItem?.PageBackground;
        set
        {
            if (CurrentPageItem != null)
            {
                CurrentPageItem.PageBackground = value;
                OnPropertyChanged(nameof(PageBackground));
            }
        }
    }

    /// <summary>
    /// 現在アクティブなページのDetailPageItemViewModelを取得します。
    /// </summary>
    public DetailPageItemViewModel? CurrentPageItem =>
        Pages.FirstOrDefault(p => p.Page == CurrentPage) ?? Pages.FirstOrDefault();

    /// <summary>
    /// 指定ページへのスクロール要求を通知するイベント
    /// </summary>
    public event Action<PdfPageModel>? ScrollToPageRequested;

    [ObservableProperty]
    private EditorToolMode _selectedTool = EditorToolMode.Pen;

    [ObservableProperty]
    private Color _selectedColor = Colors.Black;

    [ObservableProperty]
    private double _strokeThickness = 1.0;

    [ObservableProperty]
    private bool _isStraightLine;

    /// <summary>
    /// 直線トグルボタンを有効化できるか（ペンまたは蛍光ペン選択時のみtrue）
    /// </summary>
    public bool CanToggleStraightLine => SelectedTool == EditorToolMode.Pen || SelectedTool == EditorToolMode.Highlighter;

    /// <summary>
    /// 太さプリセットを変更できるか（ペン、蛍光ペン、部分消しゴム選択時のみtrue）
    /// </summary>
    public bool CanChangeThickness => SelectedTool == EditorToolMode.Pen ||
                                      SelectedTool == EditorToolMode.Highlighter ||
                                      SelectedTool == EditorToolMode.EraserPoint;

    /// <summary>
    /// カラーパレットの色を変更できるか（ペンまたは蛍光ペン選択時のみtrue）
    /// </summary>
    public bool CanChangeColor => SelectedTool == EditorToolMode.Pen || SelectedTool == EditorToolMode.Highlighter;

    /// <summary>現在選択中のツールに応じた太さプリセット一覧</summary>
    public ObservableCollection<ThicknessPresetOption> ActiveThicknessPresets { get; } = new();

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

    public bool HasPreviousPage => CurrentPage != null && _pageLookup(CurrentPage.PageNumber - 2) != null;
    public bool HasNextPage => CurrentPage != null && _pageLookup(CurrentPage.PageNumber) != null;

    /// <summary>
    /// ドキュメントを指定して初期化するメインコンストラクタ
    /// </summary>
    public DetailEditorViewModel(
        IPdfRenderer pdfRenderer,
        PdfDocumentModel? document = null)
    {
        _pdfRenderer = pdfRenderer;
        _onBackToGrid = () => { };
        _pageLookup = _ => null;

        UpdateThicknessPresets(_selectedTool);

        if (document != null)
        {
            InitializeDocument(document);
        }
    }

    /// <summary>
    /// 単一ページを対象とする初期化コンストラクタ（後方互換・単体テスト用）
    /// </summary>
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

        Pages.Add(new DetailPageItemViewModel(initialPage) { IsCurrent = true });

        UpdateThicknessPresets(_selectedTool);
        _ = LoadPageBackgroundAsync();
    }

    /// <summary>
    /// PDFドキュメントのページ構成と同期して全ページのアイテムを生成します。
    /// </summary>
    public void InitializeDocument(PdfDocumentModel document)
    {
        Pages.Clear();
        foreach (var page in document.Pages)
        {
            Pages.Add(new DetailPageItemViewModel(page));
        }

        CurrentPage = document.Pages.FirstOrDefault();
        if (Pages.Count > 0)
        {
            Pages[0].IsCurrent = true;
        }

        _ = LoadPageBackgroundAsync();
    }

    /// <summary>
    /// 指定されたページへスクロールを要求し、カレントページを更新します。
    /// </summary>
    public void ScrollToPage(PdfPageModel page)
    {
        CurrentPage = page;
        foreach (var item in Pages)
        {
            item.IsCurrent = (item.Page == page);
        }
        ScrollToPageRequested?.Invoke(page);
    }

    partial void OnZoomChanged(double value)
    {
        _ = ScheduleDynamicRender(immediate: false);
    }

    /// <summary>
    /// 指定されたページ寸法およびズーム倍率から最適なレンダリングピクセル寸法を算出します。
    /// </summary>
    public (int width, int height) CalculateRenderDimensions(PdfPageModel page, double zoom)
    {
        double scale = PtToDipScale * zoom;
        int targetWidth = Math.Clamp((int)Math.Round(page.DisplayWidth * scale), MinRenderDimension, MaxRenderDimension);
        int targetHeight = Math.Clamp((int)Math.Round(page.DisplayHeight * scale), MinRenderDimension, MaxRenderDimension);
        return (targetWidth, targetHeight);
    }

    /// <summary>
    /// 現在のズーム倍率および現在ページ寸法から最適なレンダリングピクセル寸法を算出します。
    /// </summary>
    public (int width, int height) CalculateRenderDimensions(double zoom)
    {
        var page = CurrentPage ?? Pages.FirstOrDefault()?.Page;
        if (page == null) return (MinRenderDimension, MinRenderDimension);
        return CalculateRenderDimensions(page, zoom);
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

            foreach (var item in Pages.ToList())
            {
                token.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref _renderGeneration)) return;

                var (targetWidth, targetHeight) = CalculateRenderDimensions(item.Page, Zoom);
                var rendered = await _pdfRenderer.RenderPageAsync(
                    item.Page.SourceFilePath,
                    item.Page.OriginalPageIndex,
                    targetWidth,
                    targetHeight,
                    item.Page.RenderRotation,
                    token);

                token.ThrowIfCancellationRequested();
                if (generation == Volatile.Read(ref _renderGeneration) && rendered != null)
                {
                    // ダブルバッファリング: 新画像が完全に完成した瞬間のみ差し替え
                    item.PageBackground = rendered;
                }
            }

            OnPropertyChanged(nameof(PageBackground));
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

    partial void OnSelectedColorChanged(Color value)
    {
        if (SelectedTool == EditorToolMode.Pen)
        {
            _penColor = value;
        }
        else if (SelectedTool == EditorToolMode.Highlighter)
        {
            _highlighterColor = value;
        }
    }

    partial void OnStrokeThicknessChanged(double value)
    {
        if (SelectedTool == EditorToolMode.Pen)
        {
            _penThickness = value;
        }
        else if (SelectedTool == EditorToolMode.Highlighter)
        {
            _highlighterThickness = value;
        }
        else if (SelectedTool == EditorToolMode.EraserPoint)
        {
            _eraserPointThickness = value;
        }

        UpdatePresetSelection(value);
    }

    partial void OnSelectedToolChanged(EditorToolMode value)
    {
        // ツール切り替え時は直線トグルを自動的にオフへリセット
        IsStraightLine = false;
        OnPropertyChanged(nameof(CanToggleStraightLine));
        OnPropertyChanged(nameof(CanChangeThickness));
        OnPropertyChanged(nameof(CanChangeColor));

        switch (value)
        {
            case EditorToolMode.Pen:
                SelectedColor = _penColor;
                StrokeThickness = _penThickness;
                break;
            case EditorToolMode.Highlighter:
                SelectedColor = _highlighterColor;
                StrokeThickness = _highlighterThickness;
                break;
            case EditorToolMode.EraserPoint:
                StrokeThickness = _eraserPointThickness;
                break;
        }

        UpdateThicknessPresets(value);
    }

    /// <summary>
    /// 指定されたツールモードに応じた太さプリセット一覧を更新します。
    /// </summary>
    private void UpdateThicknessPresets(EditorToolMode tool)
    {
        ActiveThicknessPresets.Clear();
        if (tool == EditorToolMode.Highlighter || tool == EditorToolMode.EraserPoint)
        {
            ActiveThicknessPresets.Add(new ThicknessPresetOption(8.0, 8, "太さ: 8.0px"));
            ActiveThicknessPresets.Add(new ThicknessPresetOption(12.0, 12, "太さ: 12.0px"));
            ActiveThicknessPresets.Add(new ThicknessPresetOption(16.0, 16, "太さ: 16.0px"));
            ActiveThicknessPresets.Add(new ThicknessPresetOption(24.0, 20, "太さ: 24.0px"));
        }
        else
        {
            ActiveThicknessPresets.Add(new ThicknessPresetOption(0.5, 3, "太さ: 0.5px"));
            ActiveThicknessPresets.Add(new ThicknessPresetOption(1.0, 5, "太さ: 1.0px"));
            ActiveThicknessPresets.Add(new ThicknessPresetOption(2.0, 8, "太さ: 2.0px"));
            ActiveThicknessPresets.Add(new ThicknessPresetOption(4.0, 13, "太さ: 4.0px"));
        }

        UpdatePresetSelection(StrokeThickness);
    }

    /// <summary>
    /// 現在の太さに合致するプリセットの選択状態を更新します。
    /// </summary>
    private void UpdatePresetSelection(double thickness)
    {
        foreach (var preset in ActiveThicknessPresets)
        {
            preset.IsSelected = Math.Abs(preset.Thickness - thickness) < 0.05;
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
        if (CurrentPage == null) return;
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
        if (CurrentPage == null) return;
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

/// <summary>
/// ツールバーに表示する太さプリセットのオプション定義
/// </summary>
public class ThicknessPresetOption : ObservableObject
{
    /// <summary>線の太さ（px）</summary>
    public double Thickness { get; }

    /// <summary>アイコン表示用の円サイズ（幅・高さ）</summary>
    public double DotSize { get; }

    /// <summary>ツールチップテキスト</summary>
    public string ToolTip { get; }

    private bool _isSelected;
    /// <summary>現在選択されているかどうか</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ThicknessPresetOption(double thickness, double dotSize, string toolTip, bool isSelected = false)
    {
        Thickness = thickness;
        DotSize = dotSize;
        ToolTip = toolTip;
        _isSelected = isSelected;
    }
}
