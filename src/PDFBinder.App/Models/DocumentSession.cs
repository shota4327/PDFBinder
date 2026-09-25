using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.Models;

/// <summary>
/// 1つの開いているPDFドキュメントのセッション状態（データ、Undo/Redo履歴、表示状態）を保持するモデルクラス
/// </summary>
public partial class DocumentSession : ObservableObject
{
    /// <summary>PDFドキュメントデータ</summary>
    public PdfDocumentModel Document { get; }

    /// <summary>ドキュメント専用のアンドゥ・リドゥ履歴サービス</summary>
    public IUndoRedoService UndoRedoService { get; }

    /// <summary>詳細エディタビューがアクティブかどうか（false時はグリッドビュー）</summary>
    [ObservableProperty]
    private bool _isDetailViewActive = true;

    /// <summary>現在表示中または選択中のページ番号（1始まり）</summary>
    [ObservableProperty]
    private int _currentPageNumber = 1;

    /// <summary>詳細エディタのズーム倍率</summary>
    [ObservableProperty]
    private double _zoomFactor = 1.0;

    /// <summary>詳細エディタの表示フィットモード（初期値: ウィンドウに合わせる）</summary>
    [ObservableProperty]
    private DetailViewFitMode _fitMode = DetailViewFitMode.FitToWindow;

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
    public DocumentSession(PdfDocumentModel document, IUndoRedoService? undoRedoService = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        UndoRedoService = undoRedoService ?? new UndoRedoService();

        Document.PropertyChanged += OnDocumentPropertyChanged;
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
}
