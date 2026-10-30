using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.Models;

/// <summary>
/// 1つの開いているPDFドキュメントのセッション状態（データ、Undo/Redo履歴、表示状態）を保持するモデルクラス
/// </summary>
public partial class DocumentSession : ObservableObject, IDisposable
{
    /// <summary>PDFドキュメントデータ</summary>
    public PdfDocumentModel Document { get; }

    /// <summary>ドキュメント専用のアンドゥ・リドゥ履歴サービス</summary>
    public IUndoRedoService UndoRedoService { get; }

    /// <summary>詳細エディタビューモデル（ドキュメント専用インスタンス）</summary>
    public DetailEditorViewModel DetailEditor { get; }

    /// <summary>詳細エディタビューがアクティブかどうか（false時はグリッドビュー）</summary>
    [ObservableProperty]
    private bool _isDetailViewActive = true;

    /// <summary>現在表示中または選択中のページ番号（1始まり）</summary>
    public int CurrentPageNumber
    {
        get => DetailEditor.CurrentPageNumber;
        set => DetailEditor.CurrentPageNumber = value;
    }

    /// <summary>詳細エディタのズーム倍率</summary>
    public double ZoomFactor
    {
        get => DetailEditor.Zoom;
        set => DetailEditor.SetZoomInternal(value);
    }

    /// <summary>詳細エディタの表示フィットモード（初期値: ウィンドウに合わせる）</summary>
    public DetailViewFitMode FitMode
    {
        get => DetailEditor.FitMode;
        set => DetailEditor.SetFitMode(value);
    }

    /// <summary>詳細エディタの表示レイアウトモード（単一ページ vs 連続スクロール）</summary>
    public DetailPageViewMode PageViewMode
    {
        get => DetailEditor.PageViewMode;
        set => DetailEditor.SetPageViewMode(value);
    }

    /// <summary>保持している垂直スクロール位置（px）</summary>
    public double SavedVerticalOffset
    {
        get => DetailEditor.SavedVerticalOffset;
        set => DetailEditor.SavedVerticalOffset = value;
    }

    /// <summary>保持している水平スクロール位置（px）</summary>
    public double SavedHorizontalOffset
    {
        get => DetailEditor.SavedHorizontalOffset;
        set => DetailEditor.SavedHorizontalOffset = value;
    }

    /// <summary>選択中のリボンタブインデックス（0: PDF編集, 1: 手書き, 2: 表示）</summary>
    [ObservableProperty]
    private int _selectedRibbonTabIndex = 0;

    /// <summary>現在アクティブなセッションかどうか</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// プルダウンおよびタイトルバー用の表示タイトル（変更がある場合は末尾に * を付与）
    /// </summary>
    public string DisplayTitle => $"{Document.FileName}{(Document.IsModified ? " *" : string.Empty)}";

    /// <summary>
    /// ドキュメントの絶対パス、または未保存時のファイル名（ツールチップ表示用）
    /// </summary>
    public string FullPathOrTitle => Document.FilePath ?? Document.FileName;

    /// <summary>画像ドキュメントかどうか</summary>
    public bool IsImage => Document.IsImage;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="document">対象のPDFドキュメントモデル</param>
    /// <param name="undoRedoService">専用のUndoRedoサービス（省略時は新規インスタンスを生成）</param>
    /// <param name="detailEditor">専用の詳細エディタViewModel（省略時は新規インスタンスを生成）</param>
    /// <param name="pdfRenderer">レンダラー（detailEditor省略時の生成用）</param>
    public DocumentSession(
        PdfDocumentModel document,
        IUndoRedoService? undoRedoService = null,
        DetailEditorViewModel? detailEditor = null,
        IPdfRenderer? pdfRenderer = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        UndoRedoService = undoRedoService ?? new UndoRedoService();
        DetailEditor = detailEditor ?? new DetailEditorViewModel(pdfRenderer ?? new PdfiumRenderer(), document, autoLoadBackground: false);

        Document.PropertyChanged += OnDocumentPropertyChanged;
        DetailEditor.PropertyChanged += OnDetailEditorPropertyChanged;
    }

    private void OnDetailEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailEditorViewModel.CurrentPageNumber))
        {
            OnPropertyChanged(nameof(CurrentPageNumber));
        }
        else if (e.PropertyName == nameof(DetailEditorViewModel.Zoom))
        {
            OnPropertyChanged(nameof(ZoomFactor));
        }
        else if (e.PropertyName == nameof(DetailEditorViewModel.FitMode))
        {
            OnPropertyChanged(nameof(FitMode));
        }
        else if (e.PropertyName == nameof(DetailEditorViewModel.PageViewMode))
        {
            OnPropertyChanged(nameof(PageViewMode));
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfDocumentModel.IsModified) ||
            e.PropertyName == nameof(PdfDocumentModel.FileName) ||
            e.PropertyName == nameof(PdfDocumentModel.FilePath) ||
            e.PropertyName == nameof(PdfDocumentModel.IsImage))
        {
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(FullPathOrTitle));
            OnPropertyChanged(nameof(IsImage));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Document.PropertyChanged -= OnDocumentPropertyChanged;
        DetailEditor.PropertyChanged -= OnDetailEditorPropertyChanged;
        UndoRedoService.Clear();
        DetailEditor.Dispose();

        foreach (var page in Document.Pages)
        {
            page.Thumbnail = null;
            page.InkStrokes.Clear();
        }
        Document.Pages.Clear();

        GC.SuppressFinalize(this);
    }
}
