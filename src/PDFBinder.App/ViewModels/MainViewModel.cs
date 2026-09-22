using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PDFBinder.App.Helpers;
using PDFBinder.App.Models;
using PDFBinder.App.Services;
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
    private readonly IPrintService _printService;
    private readonly PrintSettings _persistentPrintSettings = new();
    private CancellationTokenSource? _thumbnailCts;
    private Task? _thumbnailTask;

    /// <summary>
    /// 現在進行中のサムネイル生成タスクを取得します（単体テスト・待機検証用）。
    /// </summary>
    internal Task? CurrentThumbnailTask => _thumbnailTask;

    /// <summary>開いているすべてのドキュメントセッション</summary>
    public ObservableCollection<DocumentSession> Documents { get; } = new();

    /// <summary>現在アクティブなドキュメントセッション</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFileName))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(HasOpenDocuments))]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    [NotifyPropertyChangedFor(nameof(CanRedo))]
    private DocumentSession? _activeSession;

    /// <summary>開いているドキュメントが存在するかどうか</summary>
    public bool HasOpenDocuments => Documents.Count > 0 && ActiveSession != null;

    /// <summary>タイトルバーに表示するウィンドウタイトル</summary>
    public string WindowTitle => ActiveSession != null
        ? $"{ActiveSession.DisplayTitle} - PDF Binder"
        : "PDF Binder";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFileName))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(HasOpenDocuments))]
    private PdfDocumentModel _document = new();

    /// <summary>
    /// タイトルバー中央に表示するファイル名を取得します。
    /// 未読み込み時は空文字を返します。
    /// </summary>
    public string DisplayFileName
    {
        get
        {
            if (ActiveSession != null)
            {
                if (!string.IsNullOrEmpty(ActiveSession.Document.FilePath))
                {
                    return Path.GetFileName(ActiveSession.Document.FilePath);
                }
                if (ActiveSession.Document.Pages.Count > 0)
                {
                    return ActiveSession.Document.FileName;
                }
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(Document.FilePath))
            {
                return Path.GetFileName(Document.FilePath);
            }
            if (Document.Pages.Count > 0)
            {
                return Document.FileName;
            }
            return string.Empty;
        }
    }

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
    /// 印刷確認ダイアログ（インアプリオーバーレイ）を表示するかどうか
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecutePrint))]
    private bool _isPrintDialogVisible;

    /// <summary>
    /// 現在表示中の印刷ダイアログViewModel
    /// </summary>
    [ObservableProperty]
    private PrintViewModel? _printViewModel;

    /// <summary>
    /// サムネイル基準サイズ（初期値 = 220px）
    /// </summary>
    public const double DefaultThumbnailSize = 220.0;

    /// <summary>
    /// サムネイル最小表示サイズ（px、基準サイズの50%）
    /// </summary>
    public const double MinThumbnailSize = DefaultThumbnailSize * ZoomHelper.MinZoom;

    /// <summary>
    /// サムネイル最大表示サイズ（px、基準サイズの3200%）
    /// </summary>
    public const double MaxThumbnailSize = DefaultThumbnailSize * ZoomHelper.MaxZoom;

    /// <summary>
    /// サムネイル拡大縮小ステップ幅（px、旧実装互換用）
    /// </summary>
    public const double ThumbnailSizeStep = 20.0;

    /// <summary>
    /// サムネイル生成基準幅（px）。高倍率ズームや高DPI環境でも鮮明に表示します。
    /// </summary>
    public const int ThumbnailRenderWidth = 720;

    /// <summary>
    /// サムネイル生成基準高さ（px）。縦横比約1:1.4に基づきます。
    /// </summary>
    public const int ThumbnailRenderHeight = 1008;

    [ObservableProperty]
    private double _thumbnailSize = DefaultThumbnailSize;

    /// <summary>
    /// サムネイルをさらに拡大可能かどうかを取得します。
    /// </summary>
    public bool CanZoomInThumbnail => ThumbnailSize < MaxThumbnailSize - 0.01;

    /// <summary>
    /// サムネイルをさらに縮小可能かどうかを取得します。
    /// </summary>
    public bool CanZoomOutThumbnail => ThumbnailSize > MinThumbnailSize + 0.01;

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
        else
        {
            // 詳細ビューへ切り替わった際、フィットモードが有効であれば再計算を適用
            if (DetailEditor != null && DetailEditor.FitMode != DetailViewFitMode.None)
            {
                DetailEditor.ApplyFitMode();
            }
        }

        OnPropertyChanged(nameof(CurrentZoomText));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(CanNavigatePages));
        GoToPreviousPageCommand.NotifyCanExecuteChanged();
        GoToNextPageCommand.NotifyCanExecuteChanged();
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
        double currentZoom = ThumbnailSize / DefaultThumbnailSize;
        double nextZoom = ZoomHelper.GetNextZoomIn(currentZoom);
        ThumbnailSize = Math.Round(nextZoom * DefaultThumbnailSize, 2);
    }

    /// <summary>
    /// サムネイル表示サイズを1段階縮小します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanZoomOutThumbnail))]
    public void ZoomOutThumbnail()
    {
        double currentZoom = ThumbnailSize / DefaultThumbnailSize;
        double nextZoom = ZoomHelper.GetNextZoomOut(currentZoom);
        ThumbnailSize = Math.Round(nextZoom * DefaultThumbnailSize, 2);
    }

    /// <summary>
    /// 前のページへ移動可能かどうかを取得します（詳細ビューかつ先頭ページ以外）。
    /// </summary>
    public bool CanGoToPreviousPage => IsDetailViewActive && (DetailEditor?.CanGoToPreviousPage ?? false);

    /// <summary>
    /// 次のページへ移動可能かどうかを取得します（詳細ビューかつ末尾ページ以外）。
    /// </summary>
    public bool CanGoToNextPage => IsDetailViewActive && (DetailEditor?.CanGoToNextPage ?? false);

    /// <summary>
    /// ページ移動操作が可能かどうかを取得します（詳細ビューかつ1ページ以上存在）。
    /// </summary>
    public bool CanNavigatePages => IsDetailViewActive && Document.PageCount > 0;

    /// <summary>
    /// 現在表示中のページ番号（1-based）を取得または設定します。
    /// </summary>
    public int CurrentPageNumber
    {
        get => DetailEditor?.CurrentPageNumber ?? (Document.PageCount > 0 ? 1 : 0);
        set
        {
            if (DetailEditor != null && value != DetailEditor.CurrentPageNumber)
            {
                DetailEditor.CurrentPageNumber = value;
                OnPropertyChanged(nameof(CurrentPageNumber));
            }
        }
    }

    /// <summary>
    /// 1つ前のページへ移動します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    public void GoToPreviousPage()
    {
        DetailEditor?.GoToPreviousPageCommand.Execute(null);
    }

    /// <summary>
    /// 1つ次のページへ移動します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    public void GoToNextPage()
    {
        DetailEditor?.GoToNextPageCommand.Execute(null);
    }

    /// <summary>
    /// 指定された表示フィットモードを設定します。
    /// </summary>
    [RelayCommand]
    public void SetFitMode(DetailViewFitMode mode)
    {
        DetailEditor?.SetFitMode(mode);
    }

    [ObservableProperty]
    private bool _isLoading;

    public IUndoRedoService CurrentUndoRedoService => ActiveSession?.UndoRedoService ?? _undoRedoService;

    public bool CanUndo => CurrentUndoRedoService.CanUndo;
    public bool CanRedo => CurrentUndoRedoService.CanRedo;

    /// <summary>
    /// 未保存の変更を持つドキュメントが1つ以上存在するかどうかを取得します。
    /// </summary>
    public bool HasModifiedDocuments => Documents.Any(d => d.Document.IsModified && d.Document.Pages.Count > 0);

    public MainViewModel(
        IPdfService? pdfService = null,
        IPdfRenderer? pdfRenderer = null,
        IUndoRedoService? undoRedoService = null,
        IPrintService? printService = null)
    {
        _pdfService = pdfService ?? new PdfService();
        _pdfRenderer = pdfRenderer ?? new PdfiumRenderer();
        _undoRedoService = undoRedoService ?? new UndoRedoService();
        _printService = printService ?? new WpfPrintService();

        _detailEditor = new DetailEditorViewModel(_pdfRenderer, _document);
        _detailEditor.PropertyChanged += OnDetailEditorPropertyChanged;

        _document.PropertyChanged += OnDocumentPropertyChanged;
        _document.Pages.CollectionChanged += OnDocumentPagesCollectionChanged;

        _undoRedoService.StateChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };

        Documents.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasOpenDocuments));
            OnPropertyChanged(nameof(WindowTitle));
        };
    }

    private void OnDetailEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailEditorViewModel.FitMode))
        {
            if (ActiveSession != null && DetailEditor != null)
            {
                ActiveSession.FitMode = DetailEditor.FitMode;
            }
        }
        else if (e.PropertyName == nameof(DetailEditorViewModel.Zoom))
        {
            if (ActiveSession != null && DetailEditor != null)
            {
                ActiveSession.ZoomFactor = DetailEditor.Zoom;
            }
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
        else if (e.PropertyName == nameof(DetailEditorViewModel.CanGoToPreviousPage))
        {
            OnPropertyChanged(nameof(CanGoToPreviousPage));
            GoToPreviousPageCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(DetailEditorViewModel.CanGoToNextPage))
        {
            OnPropertyChanged(nameof(CanGoToNextPage));
            GoToNextPageCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(DetailEditorViewModel.CurrentPageNumber))
        {
            OnPropertyChanged(nameof(CurrentPageNumber));
        }
    }

    partial void OnActiveSessionChanged(DocumentSession? oldValue, DocumentSession? newValue)
    {
        if (oldValue != null)
        {
            oldValue.IsActive = false;
            oldValue.IsDetailViewActive = IsDetailViewActive;
            oldValue.CurrentPageNumber = DetailEditor?.CurrentPageNumber ?? 1;
            oldValue.ZoomFactor = DetailEditor?.Zoom ?? 1.0;
            oldValue.FitMode = DetailEditor?.FitMode ?? DetailViewFitMode.FitToWindow;
            oldValue.SelectedRibbonTabIndex = SelectedRibbonTabIndex;
            oldValue.UndoRedoService.StateChanged -= OnSessionUndoRedoStateChanged;
            oldValue.PropertyChanged -= OnSessionPropertyChanged;
        }

        if (newValue != null)
        {
            newValue.IsActive = true;
            newValue.UndoRedoService.StateChanged += OnSessionUndoRedoStateChanged;
            newValue.PropertyChanged += OnSessionPropertyChanged;

            Document = newValue.Document;
            IsDetailViewActive = newValue.IsDetailViewActive;
            SelectedRibbonTabIndex = newValue.SelectedRibbonTabIndex;

            DetailEditor?.InitializeDocument(newValue.Document);
            if (DetailEditor != null)
            {
                DetailEditor.FitMode = newValue.FitMode;
                if (newValue.FitMode != DetailViewFitMode.None)
                {
                    DetailEditor.ApplyFitMode();
                }
                else
                {
                    DetailEditor.Zoom = newValue.ZoomFactor;
                }
                DetailEditor.CurrentPageNumber = newValue.CurrentPageNumber;
            }
        }
        else
        {
            Document = new PdfDocumentModel();
            DetailEditor?.InitializeDocument(Document);
            IsDetailViewActive = true;
            SelectedRibbonTabIndex = 0;
        }

        NotifySessionStateChanged();
    }

    private void OnSessionUndoRedoStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.DisplayTitle) ||
            e.PropertyName == nameof(DocumentSession.FullPathOrTitle))
        {
            OnPropertyChanged(nameof(DisplayFileName));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private void NotifySessionStateChanged()
    {
        OnPropertyChanged(nameof(DisplayFileName));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HasOpenDocuments));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    partial void OnDocumentChanged(PdfDocumentModel? oldValue, PdfDocumentModel newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnDocumentPropertyChanged;
            oldValue.Pages.CollectionChanged -= OnDocumentPagesCollectionChanged;
        }
        newValue.PropertyChanged += OnDocumentPropertyChanged;
        newValue.Pages.CollectionChanged += OnDocumentPagesCollectionChanged;

        if (ActiveSession?.Document != newValue)
        {
            var matchedSession = Documents.FirstOrDefault(s => s.Document == newValue);
            if (matchedSession != null)
            {
                ActiveSession = matchedSession;
            }
            else if (!string.IsNullOrEmpty(newValue.FilePath) || newValue.Pages.Count > 0)
            {
                var newSession = new DocumentSession(newValue);
                Documents.Add(newSession);
                ActiveSession = newSession;
            }
        }

        DetailEditor?.InitializeDocument(newValue);
        OnPropertyChanged(nameof(DisplayFileName));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HasOpenDocuments));
        UpdateDocumentNavigationProperties();
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfDocumentModel.FilePath) ||
            e.PropertyName == nameof(PdfDocumentModel.FileName) ||
            e.PropertyName == nameof(PdfDocumentModel.IsModified))
        {
            OnPropertyChanged(nameof(DisplayFileName));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private void OnDocumentPagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(DisplayFileName));
        OnPropertyChanged(nameof(WindowTitle));
        UpdateDocumentNavigationProperties();
    }

    private void UpdateDocumentNavigationProperties()
    {
        OnPropertyChanged(nameof(CanNavigatePages));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(CurrentPageNumber));
        OnPropertyChanged(nameof(CanExecutePrint));
        GoToPreviousPageCommand.NotifyCanExecuteChanged();
        GoToNextPageCommand.NotifyCanExecuteChanged();
        ShowPrintDialogCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 指定されたドキュメントセッションへ表示を切り替えます。
    /// </summary>
    [RelayCommand]
    public void SwitchDocument(DocumentSession? session)
    {
        if (session == null || session == ActiveSession) return;
        if (!Documents.Contains(session)) return;

        ActiveSession = session;
        StatusMessage = $"{session.Document.FileName} に切り替えました。";
    }

    /// <summary>
    /// 指定されたドキュメントセッション（省略時はアクティブセッション）を閉じます。
    /// 未保存の変更がある場合は保存確認を行います。
    /// </summary>
    [RelayCommand]
    public async Task<bool> CloseDocumentAsync(DocumentSession? session = null)
    {
        var target = session ?? ActiveSession;
        if (target == null || !Documents.Contains(target)) return true;

        if (target.Document.IsModified && target.Document.Pages.Count > 0)
        {
            ActiveSession = target;
            var choice = await PromptSaveConfirmationAsync(target.Document.FileName);
            if (choice == SaveConfirmationResult.Cancel)
            {
                return false;
            }
            if (choice == SaveConfirmationResult.Save)
            {
                bool saved = await SaveDocumentSessionAsync(target);
                if (!saved)
                {
                    return false;
                }
            }
        }

        int targetIndex = Documents.IndexOf(target);
        bool wasActive = (ActiveSession == target);

        Documents.Remove(target);

        if (wasActive)
        {
            if (Documents.Count > 0)
            {
                int nextIndex = Math.Clamp(targetIndex, 0, Documents.Count - 1);
                ActiveSession = Documents[nextIndex];
            }
            else
            {
                ActiveSession = null;
            }
        }

        StatusMessage = Documents.Count > 0
            ? $"{target.Document.FileName} を閉じました。"
            : "すべてのドキュメントを閉じました。";

        return true;
    }

    /// <summary>
    /// アプリケーション終了時などに、変更のある全ドキュメントの保存確認を順次実行します。
    /// </summary>
    public async Task<bool> ConfirmSaveAllAsync()
    {
        var modifiedSessions = Documents.Where(d => d.Document.IsModified && d.Document.Pages.Count > 0).ToList();
        foreach (var session in modifiedSessions)
        {
            ActiveSession = session;
            var choice = await PromptSaveConfirmationAsync(session.Document.FileName);
            if (choice == SaveConfirmationResult.Cancel)
            {
                return false;
            }
            if (choice == SaveConfirmationResult.Save)
            {
                bool saved = await SaveDocumentSessionAsync(session);
                if (!saved)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// PDFファイルを開きます。複数選択された場合は別ドキュメントとして順次追加します。
    /// </summary>
    [RelayCommand]
    public async Task OpenDocumentAsync(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDFファイル (*.pdf)|*.pdf|すべてのファイル (*.*)|*.*",
                Title = "PDFファイルを開く",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true) return;

            foreach (var file in dialog.FileNames)
            {
                await OpenSingleDocumentAsync(file);
            }
            return;
        }

        await OpenSingleDocumentAsync(filePath);
    }

    /// <summary>
    /// 単一のPDFファイルを読み込み、新規ドキュメントセッションとして追加・アクティブ化します。
    /// 既に開かれているファイルの場合は、既存のセッションへ切り替えます。
    /// </summary>
    public async Task OpenSingleDocumentAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch
        {
            fullPath = filePath;
        }

        var existing = Documents.FirstOrDefault(d =>
            !string.IsNullOrEmpty(d.Document.FilePath) &&
            string.Equals(Path.GetFullPath(d.Document.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            ActiveSession = existing;
            StatusMessage = $"{existing.Document.FileName} を表示しました。";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "PDFを読み込んでいます...";

            var doc = await _pdfService.LoadDocumentAsync(filePath);
            var session = new DocumentSession(doc);

            // 未編集かつ0ページの「名称未設定」セッションが存在する場合はそれを除去
            var emptyUntitled = Documents.FirstOrDefault(d =>
                string.IsNullOrEmpty(d.Document.FilePath) &&
                d.Document.Pages.Count == 0 &&
                !d.Document.IsModified);
            if (emptyUntitled != null)
            {
                Documents.Remove(emptyUntitled);
            }

            Documents.Add(session);
            ActiveSession = session;
            StatusMessage = $"{doc.FileName} を読み込みました（全 {doc.PageCount} ページ）";
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
    /// 外部PDFを現在のアクティブドキュメントの末尾または任意の位置に結合・追加します。
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

        if (ActiveSession == null || Documents.Count == 0)
        {
            await OpenSingleDocumentAsync(filePath);
            return;
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
    /// ドロップされた外部ファイル群（PDFファイル）を別ドキュメントとして順次開きます。
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
            await OpenSingleDocumentAsync(file);
        }
    }

    /// <summary>
    /// 外部PDFファイル群の全ページを指定したインデックス位置に挿入・結合します（Undo/Redo対応）。
    /// </summary>
    /// <param name="filePaths">PDFファイルのパス一覧</param>
    /// <param name="insertIndex">挿入先インデックス（0始まり）</param>
    public async Task InsertPdfFilesAsync(IEnumerable<string>? filePaths, int insertIndex)
    {
        if (filePaths == null) return;

        var pdfFiles = filePaths
            .Where(f => !string.IsNullOrWhiteSpace(f) && f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pdfFiles.Count == 0) return;

        if (ActiveSession == null || Documents.Count == 0)
        {
            await HandleFileDropAsync(pdfFiles);
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "PDFページを挿入しています...";

            var pagesToInsert = new List<PdfPageModel>();
            foreach (var file in pdfFiles)
            {
                var importedDoc = await _pdfService.LoadDocumentAsync(file);
                pagesToInsert.AddRange(importedDoc.Pages);
            }

            if (pagesToInsert.Count == 0) return;

            int targetIndex = Math.Clamp(insertIndex, 0, Document.Pages.Count);
            var cmd = new InsertPagesCommand(Document, pagesToInsert, targetIndex);
            CurrentUndoRedoService.Execute(cmd);

            DetailEditor?.InitializeDocument(Document);
            _ = EnsureThumbnailsGeneratedAsync();
            StatusMessage = $"{pdfFiles.Count} 件のファイルから {pagesToInsert.Count} ページを挿入しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF挿入エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }


    [ObservableProperty]
    private bool _isSaveConfirmationVisible;

    [ObservableProperty]
    private string _saveConfirmationFileName = string.Empty;

    private TaskCompletionSource<SaveConfirmationResult>? _saveConfirmationTcs;

    /// <summary>
    /// 未保存の変更が存在する場合に保存を確認するダイアログ表示用デリゲート。
    /// 引数はファイル名、戻り値はユーザー選択結果。
    /// テスト時にモック可能。nullの場合はインアプリオーバーレイを表示します。
    /// </summary>
    public Func<string, SaveConfirmationResult>? ConfirmSavePrompt { get; set; }

    /// <summary>
    /// 未保存変更の保存確認ダイアログ（インアプリオーバーレイ）を表示し、ユーザーの選択結果を非同期に取得します。
    /// </summary>
    public async Task<SaveConfirmationResult> PromptSaveConfirmationAsync(string fileName)
    {
        if (ConfirmSavePrompt != null)
        {
            return ConfirmSavePrompt(fileName);
        }

        SaveConfirmationFileName = fileName;
        IsSaveConfirmationVisible = true;

        _saveConfirmationTcs = new TaskCompletionSource<SaveConfirmationResult>();
        return await _saveConfirmationTcs.Task;
    }

    /// <summary>
    /// 保存確認ダイアログのユーザー選択を確定し、オーバーレイを閉じます。
    /// </summary>
    [RelayCommand]
    public void ConfirmSave(SaveConfirmationResult result)
    {
        IsSaveConfirmationVisible = false;
        _saveConfirmationTcs?.TrySetResult(result);
        _saveConfirmationTcs = null;
    }

    /// <summary>
    /// 保存確認ダイアログをキャンセルして閉じます。
    /// </summary>
    [RelayCommand]
    public void CancelSaveConfirmation()
    {
        if (IsSaveConfirmationVisible)
        {
            ConfirmSave(SaveConfirmationResult.Cancel);
        }
    }

    /// <summary>
    /// 未保存変更の保存確認ダイアログを表示し、ユーザーの選択結果を取得します（同期フォールバック用）。
    /// </summary>
    public SaveConfirmationResult PromptSaveConfirmation(string fileName)
    {
        if (ConfirmSavePrompt != null)
        {
            return ConfirmSavePrompt(fileName);
        }

        var owner = Application.Current?.MainWindow;
        var result = (owner != null && owner.IsVisible)
            ? MessageBox.Show(owner, $"{fileName} への変更内容を保存しますか？", "PDF Binder", MessageBoxButton.YesNoCancel, MessageBoxImage.Question)
            : MessageBox.Show($"{fileName} への変更内容を保存しますか？", "PDF Binder", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => SaveConfirmationResult.Save,
            MessageBoxResult.No => SaveConfirmationResult.Discard,
            _ => SaveConfirmationResult.Cancel
        };
    }

    /// <summary>
    /// 未保存の変更がある場合に保存確認を行い、後続処理（別ファイル読み込みや終了）を続行してよいかを判定します。
    /// </summary>
    /// <returns>続行可能な場合はtrue、キャンセルまたは保存失敗により中断すべき場合はfalse</returns>
    public async Task<bool> ConfirmSaveAndProceedAsync()
    {
        if (!Document.IsModified || Document.Pages.Count == 0)
        {
            return true;
        }

        var choice = await PromptSaveConfirmationAsync(Document.FileName);
        switch (choice)
        {
            case SaveConfirmationResult.Cancel:
                return false;

            case SaveConfirmationResult.Discard:
                return true;

            case SaveConfirmationResult.Save:
                return await SaveDocumentAsync();

            default:
                return false;
        }
    }

    /// <summary>
    /// 上書き保存を実行します。
    /// </summary>
    /// <returns>保存に成功した場合はtrue、キャンセルまたは失敗した場合はfalse</returns>
    [RelayCommand]
    public async Task<bool> SaveDocumentAsync() => await SaveDocumentSessionAsync(ActiveSession);

    /// <summary>
    /// 名前を付けて保存を実行します。
    /// </summary>
    /// <returns>保存に成功した場合はtrue、キャンセルまたは失敗した場合はfalse</returns>
    [RelayCommand]
    public async Task<bool> SaveDocumentAsAsync() => await SaveDocumentAsSessionAsync(ActiveSession);

    /// <summary>
    /// 指定されたセッション（未指定時はアクティブセッションまたは現在のDocument）を上書き保存します。
    /// </summary>
    public async Task<bool> SaveDocumentSessionAsync(DocumentSession? session = null)
    {
        var targetDoc = session?.Document ?? ActiveSession?.Document ?? Document;

        if (string.IsNullOrEmpty(targetDoc.FilePath))
        {
            return await SaveDocumentAsSessionAsync(session);
        }

        return await ExecuteSaveForDocumentAsync(targetDoc, targetDoc.FilePath);
    }

    /// <summary>
    /// 指定されたセッション（未指定時はアクティブセッションまたは現在のDocument）を名前を付けて保存します。
    /// </summary>
    public async Task<bool> SaveDocumentAsSessionAsync(DocumentSession? session = null)
    {
        var targetDoc = session?.Document ?? ActiveSession?.Document ?? Document;

        var dialog = new SaveFileDialog
        {
            Filter = "PDFファイル (*.pdf)|*.pdf",
            Title = "PDFファイルを保存",
            FileName = targetDoc.FileName
        };

        if (dialog.ShowDialog() != true) return false;
        return await ExecuteSaveForDocumentAsync(targetDoc, dialog.FileName);
    }

    private async Task<bool> ExecuteSaveAsync(string targetPath)
    {
        return await ExecuteSaveForDocumentAsync(Document, targetPath);
    }

    private async Task<bool> ExecuteSaveForDocumentAsync(PdfDocumentModel doc, string targetPath)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "保存しています...";

            await _pdfService.SaveDocumentAsync(doc, targetPath);
            doc.FilePath = targetPath;
            doc.IsModified = false;
            StatusMessage = $"保存しました: {targetPath}";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 白紙ページを追加します。ドキュメント未読み込み時は新規「名称未設定.pdf」を作成します。
    /// </summary>
    [RelayCommand]
    public void AddBlankPage()
    {
        if (ActiveSession == null || Documents.Count == 0)
        {
            var newDoc = new PdfDocumentModel();
            var blankPage = _pdfService.CreateBlankPage(595.28, 841.89);
            var session = new DocumentSession(newDoc);
            Documents.Add(session);
            ActiveSession = session;

            var insertCmd = new InsertPageCommand(newDoc, blankPage, 0);
            session.UndoRedoService.Execute(insertCmd);

            DetailEditor?.InitializeDocument(newDoc);
            StatusMessage = "白紙ページを追加しました。";
            return;
        }

        var selected = Document.Pages.FirstOrDefault(p => p.IsSelected) ?? (IsDetailViewActive ? DetailEditor?.CurrentPage : null);
        double w = selected?.Width ?? 595.28;
        double h = selected?.Height ?? 841.89;

        var blank = _pdfService.CreateBlankPage(w, h);
        int insertIdx = selected != null ? Document.Pages.IndexOf(selected) + 1 : Document.Pages.Count;

        var cmd = new InsertPageCommand(Document, blank, insertIdx);
        CurrentUndoRedoService.Execute(cmd);

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
            CurrentUndoRedoService.Execute(new CompositeUndoableCommand(commands, "ページの回転"));
            if (!IsDetailViewActive)
            {
                _ = RefreshSelectedThumbnailsAsync(targets);
            }
            else
            {
                DetailEditor?.OnPageDimensionsChanged();
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

        CurrentUndoRedoService.Execute(new CompositeUndoableCommand(commands, "ページの削除"));
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
    /// ドキュメントの全ページをそれぞれ半分のサイズ（横長なら左右、縦長なら上下）に2分割します。
    /// </summary>
    [RelayCommand]
    public async Task SplitPagesHalfAsync()
    {
        if (Document.Pages.Count == 0) return;

        try
        {
            IsLoading = true;
            StatusMessage = "進行中のサムネイル生成を中断しています...";

            // 進行中のサムネイル生成タスクをその時点までで安全に終了し、完了を待機
            await CancelAndAwaitThumbnailsAsync();

            StatusMessage = "ページを分割しています...";

            var oldPages = Document.Pages.ToList();
            var newPages = await _pdfService.SplitPagesHalfAsync(oldPages);

            var cmd = new ReplaceAllPagesCommand(Document, oldPages, newPages);
            _undoRedoService.Execute(cmd);

            DetailEditor?.InitializeDocument(Document);

            if (IsDetailViewActive)
            {
                _ = DetailEditor?.ScheduleDynamicRender(immediate: true);
            }
            else
            {
                await EnsureThumbnailsGeneratedAsync();
            }

            StatusMessage = $"全 {oldPages.Count} ページを {newPages.Count} ページに分割しました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ページ分割エラー: {ex.Message}";
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
        CurrentUndoRedoService.Execute(cmd);
        StatusMessage = $"ページ {oldIndex + 1} を {newIndex + 1} へ移動しました。";
    }

    /// <summary>
    /// 指定されたページリストを指定した挿入位置へ一括移動します（Undo/Redo対応）。
    /// </summary>
    /// <param name="pagesToMove">移動対象のページ一覧</param>
    /// <param name="targetIndex">挿入先インデックス</param>
    public void MovePages(IReadOnlyList<PdfPageModel> pagesToMove, int targetIndex)
    {
        if (pagesToMove == null || pagesToMove.Count == 0) return;

        var currentPages = Document.Pages.ToList();
        var remaining = currentPages.Where(p => !pagesToMove.Contains(p)).ToList();

        int boundedTarget = Math.Clamp(targetIndex, 0, currentPages.Count);
        int selectedBeforeTarget = currentPages.Take(boundedTarget).Count(pagesToMove.Contains);
        int effectiveIndex = Math.Clamp(boundedTarget - selectedBeforeTarget, 0, remaining.Count);

        var newOrder = new List<PdfPageModel>(remaining);
        newOrder.InsertRange(effectiveIndex, pagesToMove);

        if (currentPages.SequenceEqual(newOrder)) return;

        var cmd = new ReorderPagesCommand(Document, currentPages, newOrder);
        CurrentUndoRedoService.Execute(cmd);
        StatusMessage = $"{pagesToMove.Count} ページを並び替えました。";
    }


    /// <summary>
    /// ページ詳細エディタを開き、指定ページへスクロールします。
    /// </summary>
    [RelayCommand]
    public void OpenPageDetail(PdfPageModel page)
    {
        IsDetailViewActive = true;
        if (DetailEditor != null && DetailEditor.Pages.Count != Document.Pages.Count)
        {
            DetailEditor.InitializeDocument(Document);
        }
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
    /// 進行中のサムネイル生成タスクをその時点までで安全に中断し、完了を待機します。
    /// </summary>
    public async Task CancelAndAwaitThumbnailsAsync()
    {
        var cts = _thumbnailCts;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄されている場合は安全に無視
            }
        }

        var task = _thumbnailTask;
        if (task != null)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // 中断例外は正常系として受け入れ
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"サムネイル中断待機例外: {ex.Message}");
            }
        }

        _thumbnailTask = null;
        _thumbnailCts = null;
    }

    /// <summary>
    /// グリッド表示に必要なサムネイルのうち、未生成または変更されたページを非同期で生成します。
    /// 既存の生成タスクが動作中の場合は中断・待機してから新タスクを開始します。
    /// </summary>
    public Task EnsureThumbnailsGeneratedAsync()
    {
        var targets = Document.Pages.Where(p => p.Thumbnail == null || p.IsThumbnailDirty).ToList();
        if (targets.Count == 0) return Task.CompletedTask;

        var oldCts = _thumbnailCts;
        try
        {
            oldCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄されている場合は無視
        }

        var newCts = new CancellationTokenSource();
        _thumbnailCts = newCts;

        var task = RunEnsureThumbnailsAsync(targets, newCts);
        _thumbnailTask = task;
        return task;
    }

    /// <summary>
    /// サムネイル生成ループを実行し、各ページをレンダリングします。中断要求があった場合はその時点で終了します。
    /// </summary>
    private async Task RunEnsureThumbnailsAsync(List<PdfPageModel> targets, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            IsLoading = true;
            StatusMessage = "サムネイルを生成しています...";

            foreach (var page in targets)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                await UpdatePageThumbnailAsync(page, token);
                page.IsThumbnailDirty = false;
            }

            if (!token.IsCancellationRequested)
            {
                StatusMessage = $"グリッド表示（全 {Document.PageCount} ページ）";
            }
        }
        catch (OperationCanceledException)
        {
            // 中断時はその時点で静かに終了
        }
        catch (Exception ex)
        {
            StatusMessage = $"サムネイル生成エラー: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_thumbnailCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    public void Undo()
    {
        CurrentUndoRedoService.Undo();
        DetailEditor?.InitializeDocument(Document);
        if (!IsDetailViewActive)
        {
            _ = EnsureThumbnailsGeneratedAsync();
        }
        else
        {
            DetailEditor?.OnPageDimensionsChanged();
            _ = DetailEditor?.ScheduleDynamicRender(immediate: true);
        }
        StatusMessage = "操作を取り消しました。";
    }

    [RelayCommand]
    public void Redo()
    {
        CurrentUndoRedoService.Redo();
        DetailEditor?.InitializeDocument(Document);
        if (!IsDetailViewActive)
        {
            _ = EnsureThumbnailsGeneratedAsync();
        }
        else
        {
            DetailEditor?.OnPageDimensionsChanged();
            _ = DetailEditor?.ScheduleDynamicRender(immediate: true);
        }
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
    private async Task UpdatePageThumbnailAsync(PdfPageModel page, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;

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
                page.RenderRotation,
                cancellationToken,
                RenderPriority.Low);
        }

        if (cancellationToken.IsCancellationRequested || baseBitmap == null) return;
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

    /// <summary>
    /// 印刷コマンドが実行可能かどうかを取得します。
    /// </summary>
    public bool CanExecutePrint => Document.Pages.Count > 0 && !IsPrintDialogVisible;

    /// <summary>
    /// 印刷確認ダイアログ（インアプリオーバーレイ）を表示します。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecutePrint))]
    public void ShowPrintDialog()
    {
        if (Document.Pages.Count == 0) return;

        int currentIdx = DetailEditor != null ? DetailEditor.CurrentPageIndex : 0;
        if (currentIdx < 0 || currentIdx >= Document.Pages.Count)
        {
            currentIdx = 0;
        }

        PrintViewModel = new PrintViewModel(
            _printService,
            _persistentPrintSettings,
            Document.Pages.Count,
            currentIdx,
            (idx, w, h, ct) => RenderPageWithInkAsync(idx, w, h, ct),
            (idx, ct) => RenderPageWithInkAsync(idx, 2480, 3508, ct));

        PrintViewModel.RequestClose += OnPrintDialogRequestClose;
        IsPrintDialogVisible = true;
    }

    /// <summary>
    /// 印刷確認ダイアログを閉じます。
    /// </summary>
    [RelayCommand]
    public void ClosePrintDialog()
    {
        PrintViewModel?.CancelCommand.Execute(null);
    }

    /// <summary>
    /// 印刷ダイアログからの終了通知を処理します。
    /// </summary>
    private void OnPrintDialogRequestClose(bool printed)
    {
        IsPrintDialogVisible = false;
        if (PrintViewModel != null)
        {
            PrintViewModel.RequestClose -= OnPrintDialogRequestClose;
            PrintViewModel.Cleanup();
            PrintViewModel = null;
        }

        if (printed)
        {
            StatusMessage = "印刷ジョブを送信しました。";
        }
    }

    /// <summary>
    /// 手書きストロークを合成した指定サイズのページビットマップを生成します。
    /// </summary>
    private async Task<BitmapSource?> RenderPageWithInkAsync(
        int pageIndex,
        int width,
        int height,
        CancellationToken ct)
    {
        if (pageIndex < 0 || pageIndex >= Document.Pages.Count) return null;
        var page = Document.Pages[pageIndex];

        BitmapSource? baseBitmap;
        if (string.IsNullOrEmpty(page.SourceFilePath))
        {
            baseBitmap = _pdfRenderer.CreateBlankPageBitmap(width, height, page.Rotation);
        }
        else
        {
            baseBitmap = await _pdfRenderer.RenderPageAsync(
                page.SourceFilePath,
                page.OriginalPageIndex,
                width,
                height,
                page.RenderRotation,
                ct,
                RenderPriority.Normal);
        }

        if (baseBitmap != null && page.InkStrokes.Count > 0)
        {
            return _pdfRenderer.CompositeStrokes(baseBitmap, page.InkStrokes, page.DisplayWidth, page.DisplayHeight);
        }

        return baseBitmap;
    }

    /// <summary>
    /// バージョン情報（About画面）を表示するための外部デリゲート（単体テスト・差し替え用）。
    /// </summary>
    public Action? ShowAboutDialogAction { get; set; }

    /// <summary>
    /// アプリケーションのバージョン情報ダイアログを表示します。
    /// </summary>
    [RelayCommand]
    public void ShowAbout()
    {
        if (ShowAboutDialogAction != null)
        {
            ShowAboutDialogAction();
            return;
        }

        var dialog = new Views.AboutDialog
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }
}

