using System.Windows.Ink;

namespace PDFBinder.Core.Models;

/// <summary>
/// 印刷実行時点のページ状態（元ファイル情報・回転・手書きインク等）を保持する不変スナップショット
/// </summary>
public sealed class PrintPageSource
{
    /// <summary>元PDFまたは画像ファイルの絶対パス（白紙ページの場合は null）</summary>
    public string? SourceFilePath { get; }

    /// <summary>元PDF内でのページ番号（0始まり）</summary>
    public int OriginalPageIndex { get; }

    /// <summary>元PDFを開くためのパスワード（保護されている場合）</summary>
    public string? SourcePassword { get; }

    /// <summary>回転を考慮した表示上のページ幅（pt）</summary>
    public double DisplayWidth { get; }

    /// <summary>回転を考慮した表示上のページ高さ（pt）</summary>
    public double DisplayHeight { get; }

    /// <summary>元PDFの初期回転に対する現在の差分回転角度</summary>
    public PageRotation RenderRotation { get; }

    /// <summary>白紙ページかどうか</summary>
    public bool IsBlankPage { get; }

    /// <summary>画像ドキュメントのページかどうか</summary>
    public bool IsImage { get; }

    /// <summary>読み込み元PDFに専用手書き注釈が含まれていたかどうか</summary>
    public bool HasSourceBinderInkAnnotation { get; }

    /// <summary>スナップショット時点で複製された手書きストロークコレクション</summary>
    public StrokeCollection InkStrokes { get; }

    /// <summary>手書きストロークを1本以上含んでいるかどうか</summary>
    public bool HasInk => InkStrokes.Count > 0;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PrintPageSource(
        string? sourceFilePath,
        int originalPageIndex,
        string? sourcePassword,
        double displayWidth,
        double displayHeight,
        PageRotation renderRotation,
        bool isBlankPage,
        bool isImage,
        bool hasSourceBinderInkAnnotation,
        StrokeCollection? inkStrokes)
    {
        SourceFilePath = sourceFilePath;
        OriginalPageIndex = originalPageIndex;
        SourcePassword = sourcePassword;
        DisplayWidth = displayWidth;
        DisplayHeight = displayHeight;
        RenderRotation = renderRotation;
        IsBlankPage = isBlankPage;
        IsImage = isImage;
        HasSourceBinderInkAnnotation = hasSourceBinderInkAnnotation;
        InkStrokes = inkStrokes?.Clone() ?? new StrokeCollection();
    }

    /// <summary>
    /// <see cref="PdfPageModel"/> から印刷用の不変スナップショットを生成します。
    /// </summary>
    public static PrintPageSource FromPageModel(PdfPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PrintPageSource(
            page.SourceFilePath,
            page.OriginalPageIndex,
            page.SourcePassword,
            page.DisplayWidth,
            page.DisplayHeight,
            page.RenderRotation,
            page.IsBlankPage,
            page.IsImage,
            page.HasSourceBinderInkAnnotation,
            page.InkStrokes);
    }
}
