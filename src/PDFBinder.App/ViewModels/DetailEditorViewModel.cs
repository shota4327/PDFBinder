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
public partial class DetailEditorViewModel : ObservableObject
{
    private readonly IPdfRenderer _pdfRenderer;
    private readonly Action _onBackToGrid;
    private readonly Func<int, PdfPageModel?> _pageLookup;
    private readonly Stack<StrokeCollection> _strokeUndoStack = new();
    private readonly Stack<StrokeCollection> _strokeRedoStack = new();

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

    /// <summary>カラーパレットプリセット</summary>
    public ObservableCollection<Color> ColorPalette { get; } = new()
    {
        Colors.Black,
        Colors.DarkRed,
        Colors.DarkBlue,
        Colors.DarkGreen,
        Colors.DarkOrange,
        Colors.Purple,
        Colors.Gold
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

    /// <summary>
    /// ページの背景ビットマップを高DPIでレンダリングします。
    /// </summary>
    public async Task LoadPageBackgroundAsync()
    {
        // 画面表示用に150 DPI相当のサイズでレンダリング
        int targetWidth = (int)(CurrentPage.Width * 1.5);
        int targetHeight = (int)(CurrentPage.Height * 1.5);

        PageBackground = await _pdfRenderer.RenderPageAsync(
            CurrentPage.SourceFilePath,
            CurrentPage.OriginalPageIndex,
            targetWidth,
            targetHeight,
            CurrentPage.Rotation);

        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
    }

    partial void OnSelectedToolChanged(EditorToolMode value)
    {
        if (value == EditorToolMode.Highlighter)
        {
            if (SelectedColor == Colors.Black)
            {
                SelectedColor = Colors.Yellow;
            }
            StrokeThickness = 12.0;
        }
        else if (value == EditorToolMode.Pen)
        {
            if (SelectedColor == Colors.Yellow)
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
}
