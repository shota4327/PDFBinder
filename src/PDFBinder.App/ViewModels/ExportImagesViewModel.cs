using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.ViewModels;

/// <summary>
/// PDFページの画像書き出しダイアログ用 ViewModel
/// </summary>
public partial class ExportImagesViewModel : ObservableObject
{
    private readonly IImageExportService _imageExportService;
    private readonly int _totalPages;
    private readonly int _currentPageIndex;
    private readonly Func<int, int, int, CancellationToken, Task<BitmapSource?>> _renderPreviewPageFunc;
    private readonly Func<int, int, int, CancellationToken, Task<BitmapSource?>> _renderExportPageFunc;
    private readonly Func<int, (double Width, double Height)> _getPageDimensionsFunc;
    private readonly string _documentTitle;

    private List<int> _targetPageIndices = new();
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _exportCts;

    [ObservableProperty]
    private ImageExportSettings _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayExportPageText))]
    private int _currentExportPageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayExportPageText))]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private int _totalExportPages;

    /// <summary>プレビュー下部に表示するページ番号テキスト（例: "1 / 5"）</summary>
    public string DisplayExportPageText => TotalExportPages > 0 ? $"{CurrentExportPageIndex + 1} / {TotalExportPages}" : "0 / 0";

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private bool _isLoadingPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private string? _rangeErrorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    private bool _isExporting;

    [ObservableProperty]
    private string _exportProgressText = string.Empty;

    [ObservableProperty]
    private double _exportProgressValue;

    /// <summary>エラーが無く画像書き出しが実行可能かどうか</summary>
    public bool CanExport => !IsExporting && string.IsNullOrEmpty(RangeErrorMessage) && TotalExportPages > 0;

    /// <summary>前のページへ移動可能かどうか</summary>
    public bool CanGoToPreviousPage => CurrentExportPageIndex > 0 && !IsExporting;

    /// <summary>次のページへ移動可能かどうか</summary>
    public bool CanGoToNextPage => CurrentExportPageIndex < TotalExportPages - 1 && !IsExporting;

    /// <summary>ファイル保存先選択ダイアログの差し替えデリゲート（テスト用）</summary>
    public Func<string, string, string, string?>? PickSaveFileFunc { get; set; }

    /// <summary>フォルダー選択ダイアログの差し替えデリゲート（テスト用）</summary>
    public Func<string, string?>? PickFolderFunc { get; set; }

    /// <summary>エラー表示コールバック（テスト用）</summary>
    public Action<string, string>? ShowErrorAction { get; set; }

    /// <summary>書き出し完了またはキャンセル時のコールバック (success, exportedCount, targetPath)</summary>
    public event Action<bool, int, string?>? RequestClose;

    public ExportImagesViewModel(
        IImageExportService imageExportService,
        ImageExportSettings settings,
        int totalPages,
        int currentPageIndex,
        Func<int, int, int, CancellationToken, Task<BitmapSource?>> renderPreviewPageFunc,
        Func<int, int, int, CancellationToken, Task<BitmapSource?>> renderExportPageFunc,
        Func<int, (double Width, double Height)> getPageDimensionsFunc,
        string? documentTitle = null)
    {
        _imageExportService = imageExportService ?? throw new ArgumentNullException(nameof(imageExportService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _totalPages = totalPages;
        _currentPageIndex = currentPageIndex;
        _renderPreviewPageFunc = renderPreviewPageFunc ?? throw new ArgumentNullException(nameof(renderPreviewPageFunc));
        _renderExportPageFunc = renderExportPageFunc ?? throw new ArgumentNullException(nameof(renderExportPageFunc));
        _getPageDimensionsFunc = getPageDimensionsFunc ?? throw new ArgumentNullException(nameof(getPageDimensionsFunc));
        _documentTitle = string.IsNullOrWhiteSpace(documentTitle) ? "Document" : documentTitle;

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        ValidateAndRefresh();
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ValidateAndRefresh();
    }

    /// <summary>
    /// 設定の入力検証を行い、対象ページ一覧およびプレビューを更新します。
    /// </summary>
    public void ValidateAndRefresh()
    {
        RangeErrorMessage = null;
        _targetPageIndices.Clear();

        if (_totalPages <= 0)
        {
            RangeErrorMessage = "書き出し可能なページがありません。";
        }
        else
        {
            UpdateTargetPages();
        }

        TotalExportPages = _targetPageIndices.Count;
        if (TotalExportPages > 0)
        {
            CurrentExportPageIndex = Math.Clamp(CurrentExportPageIndex, 0, TotalExportPages - 1);
        }
        else
        {
            CurrentExportPageIndex = 0;
            PreviewImage = null;
        }

        NotifyCommands();
        if (TotalExportPages > 0)
        {
            _ = UpdatePreviewAsync();
        }
    }

    /// <summary>
    /// 選択された範囲種別に応じて書き出し対象ページのインデックス一覧を更新します。
    /// </summary>
    private void UpdateTargetPages()
    {
        switch (Settings.RangeType)
        {
            case ImageExportRangeType.AllPages:
                _targetPageIndices.AddRange(Enumerable.Range(0, _totalPages));
                break;

            case ImageExportRangeType.CurrentPage:
                int validIdx = Math.Clamp(_currentPageIndex, 0, _totalPages - 1);
                _targetPageIndices.Add(validIdx);
                break;

            case ImageExportRangeType.Custom:
                if (!PrintLayoutCalculator.TryParsePageRange(Settings.CustomRangeText, _totalPages, out var indices, out var error))
                {
                    RangeErrorMessage = error ?? "無効なページ指定です。";
                }
                else
                {
                    _targetPageIndices.AddRange(indices);
                }
                break;
        }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        ExecuteExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 現在プレビュー表示対象のページ画像を非同期に読み込みます。
    /// </summary>
    private async Task UpdatePreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        if (_targetPageIndices.Count == 0 || CurrentExportPageIndex >= _targetPageIndices.Count)
        {
            PreviewImage = null;
            return;
        }

        int targetPage = _targetPageIndices[CurrentExportPageIndex];
        IsLoadingPreview = true;
        try
        {
            // プレビュー用に標準解像度で生成
            var bitmap = await _renderPreviewPageFunc(targetPage, 800, 1131, ct);
            if (!ct.IsCancellationRequested)
            {
                PreviewImage = bitmap;
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常終了
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoadingPreview = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage()
    {
        if (CurrentExportPageIndex > 0)
        {
            CurrentExportPageIndex--;
            NotifyCommands();
            _ = UpdatePreviewAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage()
    {
        if (CurrentExportPageIndex < TotalExportPages - 1)
        {
            CurrentExportPageIndex++;
            NotifyCommands();
            _ = UpdatePreviewAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task ExecuteExportAsync()
    {
        if (!CanExport || _targetPageIndices.Count == 0) return;

        string cleanTitle = Path.GetFileNameWithoutExtension(_documentTitle);
        string ext = Settings.Format == ImageExportFormat.Png ? "png" : "jpg";

        string? targetPath = SelectDestination(cleanTitle, ext);
        if (string.IsNullOrEmpty(targetPath)) return;

        _exportCts?.Dispose();
        _exportCts = new CancellationTokenSource();

        IsExporting = true;
        ExportProgressValue = 0;
        NotifyCommands();

        try
        {
            await ExportPagesLoopAsync(cleanTitle, ext, targetPath, _exportCts.Token);
            RequestClose?.Invoke(true, _targetPageIndices.Count, targetPath);
        }
        catch (OperationCanceledException)
        {
            ExportProgressText = "書き出しがキャンセルされました。";
        }
        catch (Exception ex)
        {
            ShowErrorAction?.Invoke("画像書き出しエラー", $"書き出し処理中にエラーが発生しました:\n{ex.Message}");
        }
        finally
        {
            IsExporting = false;
            NotifyCommands();
        }
    }

    /// <summary>
    /// 対象ページ数に応じた保存先（1ページならファイルパス、複数ページならフォルダーパス）をユーザーに問い合わせます。
    /// </summary>
    private string? SelectDestination(string cleanTitle, string ext)
    {
        if (_targetPageIndices.Count == 1)
        {
            int pageNum = _targetPageIndices[0] + 1;
            string defaultName = $"{cleanTitle}_page_{pageNum:D3}.{ext}";
            string filter = Settings.Format == ImageExportFormat.Png
                ? "PNG画像 (*.png)|*.png"
                : "JPEG画像 (*.jpg;*.jpeg)|*.jpg;*.jpeg";

            return PickSaveFileFunc != null
                ? PickSaveFileFunc(defaultName, filter, "画像として保存")
                : ShowSaveFileDialog(defaultName, filter);
        }

        return PickFolderFunc != null
            ? PickFolderFunc("画像の保存先フォルダーの選択")
            : ShowFolderDialog();
    }

    private static string? ShowSaveFileDialog(string defaultName, string filter)
    {
        var dlg = new SaveFileDialog
        {
            FileName = defaultName,
            Filter = filter,
            Title = "画像として保存"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static string? ShowFolderDialog()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "画像の保存先フォルダーの選択"
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    /// <summary>
    /// 対象ページ一覧を指定解像度で順次レンダリングし保存します。
    /// </summary>
    private async Task ExportPagesLoopAsync(
        string cleanTitle,
        string ext,
        string targetPath,
        CancellationToken ct)
    {
        int total = _targetPageIndices.Count;
        int dpiValue = (int)Settings.Dpi;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();

            int pageIdx = _targetPageIndices[i];
            ExportProgressText = $"ページ {i + 1} / {total} を書き出し中...";
            ExportProgressValue = (double)i / total * 100;

            var (ptW, ptH) = _getPageDimensionsFunc(pageIdx);
            var (pxW, pxH) = _imageExportService.CalculatePixelSize(ptW, ptH, dpiValue);

            var bitmap = await _renderExportPageFunc(pageIdx, pxW, pxH, ct);
            if (bitmap == null)
            {
                throw new InvalidOperationException($"ページ {pageIdx + 1} のレンダリングに失敗しました。");
            }

            string outFilePath = total == 1
                ? targetPath
                : Path.Combine(targetPath, $"{cleanTitle}_page_{pageIdx + 1:D3}.{ext}");

            await _imageExportService.SaveImageAsync(bitmap, outFilePath, Settings.Format, dpiValue, quality: 90);
            ExportProgressValue = (double)(i + 1) / total * 100;
        }

        ExportProgressText = "書き出し完了";
    }

    [RelayCommand]
    public void Cancel()
    {
        if (IsExporting)
        {
            _exportCts?.Cancel();
        }
        else
        {
            _previewCts?.Cancel();
            RequestClose?.Invoke(false, 0, null);
        }
    }

    /// <summary>
    /// 登録されたイベントハンドラーや非同期トークンを破棄します。
    /// </summary>
    public void Cleanup()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
        _exportCts?.Cancel();
        _exportCts?.Dispose();
        _exportCts = null;
    }
}
