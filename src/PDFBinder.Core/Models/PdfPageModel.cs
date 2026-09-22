using System.Windows.Ink;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PDFBinder.Core.Helpers;

namespace PDFBinder.Core.Models;

/// <summary>
/// PDFの個別ページを表現するモデルクラス
/// </summary>
public partial class PdfPageModel : ObservableObject
{
    /// <summary>ページのユニーク識別子</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>元PDFファイルの絶対パス（白紙ページの場合はnull）</summary>
    public string? SourceFilePath { get; set; }

    /// <summary>元PDF内でのページ番号（0始まり）</summary>
    public int OriginalPageIndex { get; set; }

    /// <summary>白紙ページかどうかのフラグ</summary>
    public bool IsBlankPage => string.IsNullOrEmpty(SourceFilePath);

    /// <summary>元PDF読み込み時の初期回転角度</summary>
    public PageRotation OriginalRotation { get; set; } = PageRotation.Rotate0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenderRotation))]
    [NotifyPropertyChangedFor(nameof(DisplayWidth))]
    [NotifyPropertyChangedFor(nameof(DisplayHeight))]
    private PageRotation _rotation = PageRotation.Rotate0;

    [ObservableProperty]
    private double _width = 595.28; // A4標準幅（pt）

    [ObservableProperty]
    private double _height = 841.89; // A4標準高さ（pt）

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private int _pageNumber = 1;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isThumbnailDirty;

    partial void OnRotationChanged(PageRotation value)
    {
        IsModified = true;
        IsThumbnailDirty = true;
    }

    private StrokeCollection _inkStrokes = new();

    /// <summary>ユーザーによる手書きストロークコレクション</summary>
    public StrokeCollection InkStrokes
    {
        get => _inkStrokes;
        set
        {
            if (_inkStrokes == value) return;
            if (_inkStrokes != null)
            {
                _inkStrokes.StrokesChanged -= OnInkStrokesChanged;
            }
            _inkStrokes = value ?? new StrokeCollection();
            _inkStrokes.StrokesChanged += OnInkStrokesChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PdfPageModel()
    {
        _inkStrokes.StrokesChanged += OnInkStrokesChanged;
    }

    private void OnInkStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        IsModified = true;
        IsThumbnailDirty = true;
    }

    /// <summary>
    /// ページを指定した回転角度へ変更し、手書きインクも追従回転します。
    /// </summary>
    /// <param name="newRotation">新しい回転角度</param>
    public void RotateTo(PageRotation newRotation)
    {
        if (Rotation == newRotation) return;

        int deltaDeg = ((int)newRotation - (int)Rotation + 360) % 360;
        var delta = PageRotationExtensions.FromDegrees(deltaDeg);
        InkTransformHelper.RotateStrokes(InkStrokes, delta, DisplayWidth, DisplayHeight);
        if (Thumbnail != null && deltaDeg != 0)
        {
            Thumbnail = BitmapTransformHelper.CreateRotatedBitmap(Thumbnail, deltaDeg);
        }
        Rotation = newRotation;
    }

    /// <summary>
    /// ページを時計回りに90度回転します。
    /// </summary>
    public void RotateClockwise() => RotateTo(Rotation.RotateClockwise());

    /// <summary>
    /// ページを反時計回りに90度回転します。
    /// </summary>
    public void RotateCounterClockwise() => RotateTo(Rotation.RotateCounterClockwise());

    /// <summary>
    /// ページの現在の表示上の幅（回転を考慮）を取得します。
    /// </summary>
    public double DisplayWidth =>
        Rotation is PageRotation.Rotate90 or PageRotation.Rotate270 ? Height : Width;

    /// <summary>
    /// ページの現在の表示上の高さ（回転を考慮）を取得します。
    /// </summary>
    public double DisplayHeight =>
        Rotation is PageRotation.Rotate90 or PageRotation.Rotate270 ? Width : Height;

    /// <summary>
    /// 元PDFの初期回転（OriginalRotation）に対する現在の回転（Rotation）の差分回転角度を取得します。
    /// PDFium（DocLib）は元PDFの内部回転を自動的に反映してレンダリングするため、
    /// アプリ側で重ねて適用すべき回転量は差分角度のみとなります。
    /// </summary>
    public PageRotation RenderRotation =>
        PageRotationExtensions.FromDegrees(((int)Rotation - (int)OriginalRotation + 360) % 360);
}
