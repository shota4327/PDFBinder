using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PDFBinder.Core.Models;

/// <summary>
/// 現在開いているPDFドキュメント全体を表現するモデルクラス
/// </summary>
public partial class PdfDocumentModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string? _filePath;

    [ObservableProperty]
    private bool _isModified;

    /// <summary>ドキュメント内の全ページコレクション</summary>
    public ObservableCollection<PdfPageModel> Pages { get; } = new();

    /// <summary>現在開いているファイル名（未保存時は名称未設定）</summary>
    public string FileName => string.IsNullOrEmpty(FilePath)
        ? "名称未設定.pdf"
        : Path.GetFileName(FilePath);

    /// <summary>ページ数</summary>
    public int PageCount => Pages.Count;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PdfDocumentModel()
    {
        Pages.CollectionChanged += OnPagesCollectionChanged;
    }

    private void OnPagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (PdfPageModel page in e.OldItems)
            {
                page.PropertyChanged -= OnPagePropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (PdfPageModel page in e.NewItems)
            {
                page.PropertyChanged += OnPagePropertyChanged;
            }
        }

        UpdatePageNumbers();
        IsModified = true;
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfPageModel.IsModified) && sender is PdfPageModel { IsModified: true })
        {
            IsModified = true;
        }
    }

    /// <summary>
    /// 全ページの表示用ページ番号（1始まり）を更新します。
    /// </summary>
    public void UpdatePageNumbers()
    {
        for (int i = 0; i < Pages.Count; i++)
        {
            Pages[i].PageNumber = i + 1;
        }
        OnPropertyChanged(nameof(PageCount));
    }

    /// <summary>
    /// ページを末尾に追加します。
    /// </summary>
    public void AddPage(PdfPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Pages.Add(page);
    }

    /// <summary>
    /// 任意の位置にページを挿入します。
    /// </summary>
    public void InsertPage(int index, PdfPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (index < 0 || index > Pages.Count)
        {
            Pages.Add(page);
        }
        else
        {
            Pages.Insert(index, page);
        }
    }

    /// <summary>
    /// 指定したページをドキュメントから削除します。
    /// </summary>
    public bool RemovePage(PdfPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Pages.Remove(page);
    }

    /// <summary>
    /// ページの並び順を変更（移動）します。
    /// </summary>
    public void MovePage(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Pages.Count ||
            newIndex < 0 || newIndex >= Pages.Count ||
            oldIndex == newIndex)
        {
            return;
        }

        Pages.Move(oldIndex, newIndex);
    }

    /// <summary>
    /// ドキュメントおよび全ページの変更状態（未保存フラグおよびサムネイル更新フラグ）を初期化します。
    /// </summary>
    public void ResetModifiedState()
    {
        IsModified = false;
        foreach (var page in Pages)
        {
            page.IsModified = false;
            page.IsThumbnailDirty = false;
        }
    }

    /// <summary>
    /// ドキュメントをクリアします。
    /// </summary>
    public void Clear()
    {
        foreach (var page in Pages)
        {
            page.PropertyChanged -= OnPagePropertyChanged;
        }
        Pages.Clear();
        FilePath = null;
        ResetModifiedState();
    }
}
