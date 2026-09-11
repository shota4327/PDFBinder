using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.ViewModels;

/// <summary>
/// アプリケーション全体のメインViewModel
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IPdfService _pdfService;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly IUndoRedoService _undoRedoService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFileName))]
    private PdfDocumentModel _document = new();

    /// <summary>
    /// タイトルバー中央に表示するファイル名を取得します。
    /// 未読み込み時は空文字を返します。
    /// </summary>
    public string DisplayFileName => string.IsNullOrEmpty(Document.FilePath)
        ? string.Empty
        : Path.GetFileName(Document.FilePath);

    [ObservableProperty]
    private bool _isDetailViewActive = true;

    /// <summary>
    /// 現在選択中のリボンタブのインデックス（0: PDF編集, 1: 手書き, 2: 表示）
    /// </summary>
    [ObservableProperty]
    private int _selectedRibbonTabIndex = 0;

    [ObservableProperty]
    private DetailEditorViewModel? _detailEditor;

    [ObservableProperty]
    private string _statusMessage = "PDFファイルを開くか、ドラッグ＆ドロップしてください。";

    /// <summary>
    /// 外部ファイルドラッグ中にドロップ案内オーバーレイを表示するかどうか
    /// </summary>
    [ObservableProperty]
    private bool _isDragOver;

    /// <summary>
    /// サムネイル最小表示サイズ（px）
    /// </summary>
    public const double MinThumbnailSize = 140.0;

    /// <summary>
    /// サムネイル最大表示サイズ（px）
    /// </summary>
    public const double MaxThumbnailSize = 360.0;

    /// <summary>
    /// サムネイル拡大縮小ステップ幅（px）
    /// </summary>
    public const double ThumbnailSizeStep = 20.0;

    /// <summary>
    /// サムネイル基準サイズ（初期値 = 220px）
    /// </summary>
    public const double DefaultThumbnailSize = 220.0;

    /// <summary>
    /// サムネイル生成基準幅（px）。最大表示サイズ（360px）や高DPI環境でも鮮明に表示します。
    /// </summary>
    public const int ThumbnailRenderWidth = 360;

    /// <summary>
    /// サムネイル生成基準高さ（px）。縦横比約1:1.4に基づきます。
    /// </summary>
    public const int ThumbnailRenderHeight = 504;

    [ObservableProperty]
    private double _thumbnailSize = DefaultThumbnailSize;

    /// <summary>
    /// サムネイルをさらに拡大可能かどうかを取得します。
    /// </summary>
    public bool CanZoomInThumbnail => ThumbnailSize < MaxThumbnailSize;

    /// <summary>
    /// サムネイルをさらに縮小可能かどうかを取得します。
    /// </summary>
    public bool CanZoomOutThumbnail => ThumbnailSize > MinThumbnailSize;

    partial void OnThumbnailSizeChanged(double value)
    {
        ZoomInThumbnailCommand.NotifyCanExecuteChanged();
        ZoomOutThumbnailCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CurrentZoomText));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    partial void OnIsDetailViewActiveChanged(bool value)
    {
        if (!value)
        {
            // グリッドビューに切り替わった場合
            // 1. 手書きタブ（1）を開いていた場合は表示タブ（2）へ自動切り替え
            if (SelectedRibbonTabIndex == 1)
            {
                SelectedRibbonTabIndex = 2;
            }

            // 2. 未生成または編集済みのサムネイルがあればオンデマンド生成
            _ = EnsureThumbnailsGeneratedAsync();
        }

        OnPropertyChanged(nameof(CurrentZoomText));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    /// <summary>
    /// 現在のアクティブビューに応じた拡大率表示文字列を取得します。
    /// </summary>
    public string CurrentZoomText => IsDetailViewActive
        ? (DetailEditor != null ? $"{DetailEditor.Zoom:P0}" : "100%")
        : $"{(ThumbnailSize / DefaultThumbnailSize):P0}";

    /// <summary>
    /// 現在のビューでさらに拡大可能かどうかを取得します。
    /// </summary>
    public bool CanZoomIn => IsDetailViewActive
        ? (DetailEditor != null && DetailEditor.Zoom < DetailEditorViewModel.MaxZoom)
        : CanZoomInThumbnail;

    /// <summary>
    /// 現在のビューでさらに縮小可能かどうかを取得します。
    /// </summary>
    public bool CanZoomOut => IsDetailViewActive
        ? (DetailEditor != null && DetailEditor.Zoom > DetailEditorViewModel.MinZoom)
        : CanZoomOutThumbnail;

    /// <summary>
    /// 現在のアクティブビューを1段階拡大します。
    /// </summary>
    [RelayCommand]
    public void ZoomIn()
    {
        if (IsDetailViewActive)
        {
            DetailEditor?.ZoomInCommand.Execute(null);
        }
        else
        {
            ZoomInThumbnail();
        }
        OnPropertyChanged(nameof(CurrentZoomText));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    /// <summary>
    /// 現在のアクティブビューを1段階縮小します。
    /// </summary>
    [RelayCommand]
    public void ZoomOut()
    {
        if (IsDetailViewActive)
        {
            DetailEditor?.ZoomOutCommand.Execute(null);
        }
        else
        {
            ZoomOutThumbnail();
        }
        OnPropertyChanged(nameof(CurrentZoomText));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    /// <summary>
    /// 現在のアクティブビューの拡大率を等倍（100% / 標準サイズ）にリセットします。
    /// </summary>
    [RelayCommand]
    public void ZoomReset()
    {
        if (IsDetailViewActive)
        {
            DetailEditor?.ZoomResetCommand.Execute(null);
        }
        else
        {
            ThumbnailSize = DefaultThumbnailSize;
        }
        OnPropertyChanged(nameof(CurrentZoomText));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    /// <summary>
    /// サムネイル表示サイズを1段階拡大します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanZoomInThumbnail))]
    public void ZoomInThumbnail()
    {
        ThumbnailSize = Math.Min(MaxThumbnailSize, ThumbnailSize + ThumbnailSizeStep);
    }

    /// <summary>
    /// サムネイル表示サイズを1段階縮小します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanZoomOutThumbnail))]
    public void ZoomOutThumbnail()
    {
        ThumbnailSize = Math.Max(MinThumbnailSize, ThumbnailSize - ThumbnailSizeStep);
    }

    [ObservableProperty]
    private bool _isLoading;

    public bool CanUndo => _undoRedoService.CanUndo;
    public bool CanRedo => _undoRedoService.CanRedo;

    public MainViewModel(
        IPdfService? pdfService = null,
        IPdfRenderer? pdfRenderer = null,
        IUndoRedoService? undoRedoService = null)
    {
        _pdfService = pdfService ?? new PdfService();
        _pdfRenderer = pdfRenderer ?? new PdfiumRenderer();
        _undoRedoService = undoRedoService ?? new UndoRedoService();

        _detailEditor = new DetailEditorViewModel(_pdfRenderer, _document);
        _detailEditor.PropertyChanged += OnDetailEditorPropertyChanged;

        _document.PropertyChanged += OnDocumentPropertyChanged;

        _undoRedoService.StateChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        };
    }

    private void OnDetailEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailEditorViewModel.Zoom))
        {
            OnPropertyChanged(nameof(CurrentZoomText));
            OnPropertyChanged(nameof(CanZoomIn));
            OnPropertyChanged(nameof(CanZoomOut));
        }
        else if (e.PropertyName == nameof(DetailEditorViewModel.CurrentPage))
        {
            if (DetailEditor?.CurrentPage != null && IsDetailViewActive)
            {
                StatusMessage = $"ページ {DetailEditor.CurrentPage.PageNumber} / {Document.PageCount}";
            }
        }
    }

    partial void OnDocumentChanged(PdfDocumentModel? oldValue, PdfDocumentModel newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnDocumentPropertyChanged;
        }
        newValue.PropertyChanged += OnDocumentPropertyChanged;
        DetailEditor?.InitializeDocument(newValue);
        OnPropertyChanged(nameof(DisplayFileName));
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfDocumentModel.FilePath) || e.PropertyName == nameof(PdfDocumentModel.FileName))
        {
            OnPropertyChanged(nameof(DisplayFileName));
        }
    }

    /// <summary>
    /// PDFファイルを開きます。
    /// </summary>
    [RelayCommand]
    public async Task OpenDocumentAsync(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDFファイル (*.pdf)|*.pdf|すべてのファイル (*.*)|*.*",
                Title = "PDFファイルを開く"
            };

            if (dialog.ShowDialog() != true) return;
            filePath = dialog.FileName;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "PDFを読み込んでいます...";

            var doc = await _pdfService.LoadDocumentAsync(filePath);
            Document = doc;
            _undoRedoService.Clear();

            IsDetailViewActive = true;
            DetailEditor?.InitializeDocument(doc);
            StatusMessage = $"{Document.FileName} を読み込みました（全 {Document.PageCount} ページ）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 外部PDFを末尾または任意の位置に結合・追加します。
    /// </summary>
    [RelayCommand]
    public async Task AppendDocumentAsync(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDFファイル (*.pdf)|*.pdf|すべてのファイル (*.*)|*.*",
                Title = "結合するPDFファイルを選択"
            };

            if (dialog.ShowDialog() != true) return;
            filePath = dialog.FileName;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "PDFを追加・結合しています...";

            int prevCount = Document.PageCount;
            await _pdfService.AppendDocumentAsync(Document, filePath);

            DetailEditor?.InitializeDocument(Document);
            StatusMessage = $"{Path.GetFileName(filePath)} を結合しました（合計 {Document.PageCount} ページ）";
            if (!IsDetailViewActive)
            {
                _ = GenerateThumbnailsAsync(prevCount);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"結合エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// ドロップされた外部ファイル群（PDFファイル）を順次読み込み・結合処理します。
    /// 未読み込み時は先頭ファイルを新規オープンし、以降のファイルを末尾に順次結合します。
    /// </summary>
    /// <param name="filePaths">ドロップされたファイルパス一覧</param>
    public async Task HandleFileDropAsync(IEnumerable<string>? filePaths)
    {
        if (filePaths == null) return;

        var pdfFiles = filePaths
            .Where(f => !string.IsNullOrWhiteSpace(f) && f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pdfFiles.Count == 0) return;

        foreach (var file in pdfFiles)
        {
            if (Document.Pages.Count == 0)
            {
                await OpenDocumentAsync(file);
            }
            else
            {
                await AppendDocumentAsync(file);
            }
        }
    }

    /// <summary>
    /// 上書き保存を実行します。
    /// </summary>
    [RelayCommand]
    public async Task SaveDocumentAsync()
    {
        if (string.IsNullOrEmpty(Document.FilePath))
        {
            await SaveDocumentAsAsync();
            return;
        }

        await ExecuteSaveAsync(Document.FilePath);
    }

    /// <summary>
    /// 名前を付けて保存を実行します。
    /// </summary>
    [RelayCommand]
    public async Task SaveDocumentAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PDFファイル (*.pdf)|*.pdf",
            Title = "PDFファイルを保存",
            FileName = Document.FileName
        };

        if (dialog.ShowDialog() != true) return;
        await ExecuteSaveAsync(dialog.FileName);
    }

    private async Task ExecuteSaveAsync(string targetPath)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "保存しています...";

            await _pdfService.SaveDocumentAsync(Document, targetPath);
            StatusMessage = $"保存しました: {targetPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 白紙ページを追加します。
    /// </summary>
    [RelayCommand]
    public void AddBlankPage()
    {
        var selected = Document.Pages.FirstOrDefault(p => p.IsSelected) ?? (IsDetailViewActive ? DetailEditor?.CurrentPage : null);
        double w = selected?.Width ?? 595.28;
        double h = selected?.Height ?? 841.89;

        var blank = _pdfService.CreateBlankPage(w, h);
        int insertIdx = selected != null ? Document.Pages.IndexOf(selected) + 1 : Document.Pages.Count;

        var cmd = new InsertPageCommand(Document, blank, insertIdx);
        _undoRedoService.Execute(cmd);

        if (!IsDetailViewActive)
        {
            blank.Thumbnail = _pdfRenderer.CreateBlankPageBitmap(ThumbnailRenderWidth, ThumbnailRenderHeight, blank.Rotation);
        }
        DetailEditor?.InitializeDocument(Document);
        StatusMessage = "白紙ページを追加しました。";
    }

    /// <summary>
    /// 選択中のページ（または全ページ）を時計回りに90度回転します。
    /// </summary>
    [RelayCommand]
    public void RotateClockwise() => RotateSelected(p => p.Rotation.RotateClockwise());

    /// <summary>
    /// 選択中のページ（または全ページ）を反時計回りに90度回転します。
    /// </summary>
    [RelayCommand]
    public void RotateCounterClockwise() => RotateSelected(p => p.Rotation.RotateCounterClockwise());

    private void RotateSelected(Func<PdfPageModel, PageRotation> nextRotation)
    {
        var targets = Document.Pages.Where(p => p.IsSelected).ToList();
        if (targets.Count == 0 && IsDetailViewActive && DetailEditor?.CurrentPage != null)
        {
            targets = new List<PdfPageModel> { DetailEditor.CurrentPage };
        }
        else if (targets.Count == 0 && Document.Pages.Count > 0)
        {
            targets = Document.Pages.ToList();
        }

        var commands = new List<IUndoableCommand>();
        foreach (var page in targets)
        {
            var oldRot = page.Rotation;
            var newRot = nextRotation(page);
            commands.Add(new RotatePageCommand(page, oldRot, newRot));
        }

        if (commands.Count > 0)
        {
            _undoRedoService.Execute(new CompositeUndoableCommand(commands, "ページの回転"));
            if (!IsDetailViewActive)
            {
                _ = RefreshSelectedThumbnailsAsync(targets);
            }
            else
            {
                _ = DetailEditor?.ScheduleDynamicRender(immediate: true);
            }
            StatusMessage = $"{targets.Count} ページを回転しました。";
        }
    }

    /// <summary>
    /// 選択中のページを削除します。
    /// </summary>
    [RelayCommand]
    public void DeleteSelectedPages()
    {
        var targets = Document.Pages.Where(p => p.IsSelected).ToList();
        if (targets.Count == 0 && IsDetailViewActive && DetailEditor?.CurrentPage != null)
        {
            targets = new List<PdfPageModel> { DetailEditor.CurrentPage };
        }
        if (targets.Count == 0) return;

        var commands = new List<IUndoableCommand>();
        foreach (var page in targets)
        {
            int idx = Document.Pages.IndexOf(page);
            commands.Add(new RemovePageCommand(Document, page, idx));
        }

        _undoRedoService.Execute(new CompositeUndoableCommand(commands, "ページの削除"));
        DetailEditor?.InitializeDocument(Document);
        StatusMessage = $"{targets.Count} ページを削除しました。";
    }

    /// <summary>
    /// 選択したページを別PDFファイルとして抽出（分割）します。
    /// </summary>
    [RelayCommand]
    public async Task ExportSelectedPagesAsync()
    {
        var targets = Document.Pages.Where(p => p.IsSelected).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "エクスポートするページを選択してください。";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDFファイル (*.pdf)|*.pdf",
            Title = "選択したページを保存",
            FileName = $"{Path.GetFileNameWithoutExtension(Document.FileName)}_抽出.pdf"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsLoading = true;
            StatusMessage = "抽出したページを保存しています...";
            await _pdfService.ExportPagesAsync(targets, dialog.FileName);
            StatusMessage = $"{targets.Count} ページを書き出しました: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エクスポートエラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 全ページを1ページずつの個別PDFに一括分割します。
    /// </summary>
    [RelayCommand]
    public async Task SplitAllPagesAsync()
    {
        if (Document.Pages.Count == 0) return;

        var dialog = new OpenFolderDialog
        {
            Title = "分割先フォルダの選択"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsLoading = true;
            StatusMessage = "全ページを分割しています...";
            int count = await _pdfService.SplitAllPagesAsync(Document, dialog.FolderName, Document.FileName);
            StatusMessage = $"{count} 件の個別PDFに分割しました: {dialog.FolderName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"分割エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// ページの並び替えを実行し、アンドゥ履歴に記録します。
    /// </summary>
    public void MovePage(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex) return;
        var cmd = new MovePageCommand(Document, oldIndex, newIndex);
        _undoRedoService.Execute(cmd);
        StatusMessage = $"ページ {oldIndex + 1} を {newIndex + 1} へ移動しました。";
    }

    /// <summary>
    /// ページ詳細エディタを開き、指定ページへスクロールします。
    /// </summary>
    [RelayCommand]
    public void OpenPageDetail(PdfPageModel page)
    {
        IsDetailViewActive = true;
        DetailEditor?.ScrollToPage(page);
        SelectedRibbonTabIndex = 1;
        StatusMessage = $"ページ {page.PageNumber} を編集しています。";
    }

    /// <summary>
    /// ページ詳細エディタを閉じ、グリッドビューに戻ります。
    /// </summary>
    [RelayCommand]
    public void ClosePageDetail()
    {
        IsDetailViewActive = false;
        StatusMessage = "グリッド表示に戻りました。";
    }

    /// <summary>
    /// グリッド表示に必要なサムネイルのうち、未生成または変更されたページを非同期で生成します。
    /// </summary>
    public async Task EnsureThumbnailsGeneratedAsync()
    {
        var targets = Document.Pages.Where(p => p.Thumbnail == null || p.IsModified).ToList();
        if (targets.Count == 0) return;

        try
        {
            IsLoading = true;
            StatusMessage = "サムネイルを生成しています...";

            foreach (var page in targets)
            {
                await UpdatePageThumbnailAsync(page);
                page.IsModified = false;
            }

            StatusMessage = $"グリッド表示（全 {Document.PageCount} ページ）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"サムネイル生成エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void Undo()
    {
        _undoRedoService.Undo();
        StatusMessage = "操作を取り消しました。";
    }

    [RelayCommand]
    public void Redo()
    {
        _undoRedoService.Redo();
        StatusMessage = "操作をやり直しました。";
    }

    /// <summary>
    /// バックグラウンドでサムネイル画像を生成します。
    /// </summary>
    private async Task GenerateThumbnailsAsync(int startIndex = 0)
    {
        for (int i = startIndex; i < Document.Pages.Count; i++)
        {
            var page = Document.Pages[i];
            if (page.Thumbnail == null)
            {
                await UpdatePageThumbnailAsync(page);
            }
        }
    }

    /// <summary>
    /// 指定されたページ群のサムネイル画像を再生成・更新します。
    /// </summary>
    private async Task RefreshSelectedThumbnailsAsync(IEnumerable<PdfPageModel> pages)
    {
        foreach (var page in pages)
        {
            await UpdatePageThumbnailAsync(page);
        }
    }

    /// <summary>
    /// 単一ページのサムネイル画像をレンダリングし、手書きストロークが存在する場合は合成して設定します。
    /// </summary>
    private async Task UpdatePageThumbnailAsync(PdfPageModel page)
    {
        BitmapSource? baseBitmap;
        if (string.IsNullOrEmpty(page.SourceFilePath))
        {
            baseBitmap = _pdfRenderer.CreateBlankPageBitmap(
                ThumbnailRenderWidth,
                ThumbnailRenderHeight,
                page.Rotation);
        }
        else
        {
            baseBitmap = await _pdfRenderer.RenderPageAsync(
                page.SourceFilePath,
                page.OriginalPageIndex,
                ThumbnailRenderWidth,
                ThumbnailRenderHeight,
                page.RenderRotation);
        }

        if (baseBitmap != null)
        {
            if (page.InkStrokes.Count > 0)
            {
                page.Thumbnail = _pdfRenderer.CompositeStrokes(
                    baseBitmap,
                    page.InkStrokes,
                    page.DisplayWidth,
                    page.DisplayHeight);
            }
            else
            {
                page.Thumbnail = baseBitmap;
            }
        }
    }
}
