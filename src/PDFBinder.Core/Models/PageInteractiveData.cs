using System.Text;
using System.Windows;

namespace PDFBinder.Core.Models;

/// <summary>
/// 1ページ分のテキスト・文字座標およびリンク注釈データを集約するコンテナモデル
/// </summary>
public class PageInteractiveData
{
    /// <summary>ページ内のすべての文字情報（インデックス順）</summary>
    public IReadOnlyList<PdfTextCharacter> Characters { get; }

    /// <summary>ページ内のすべてのリンク注釈情報</summary>
    public IReadOnlyList<PdfLinkAnnotation> Links { get; }

    /// <summary>抽出時の回転角度</summary>
    public PageRotation Rotation { get; }

    /// <summary>ページ全体の平文テキスト</summary>
    public string FullText { get; }

    /// <summary>
    /// 新しいインスタンスを生成します。
    /// </summary>
    public PageInteractiveData(
        IReadOnlyList<PdfTextCharacter> characters,
        IReadOnlyList<PdfLinkAnnotation> links,
        PageRotation rotation = PageRotation.Rotate0)
    {
        Characters = characters ?? Array.Empty<PdfTextCharacter>();
        Links = links ?? Array.Empty<PdfLinkAnnotation>();
        Rotation = rotation;

        var sb = new StringBuilder(Characters.Count);
        foreach (var c in Characters)
        {
            sb.Append(c.Character);
        }
        FullText = sb.ToString();
    }

    /// <summary>
    /// 空のインタラクティブデータを返します。
    /// </summary>
    public static PageInteractiveData Empty { get; } = new(
        Array.Empty<PdfTextCharacter>(),
        Array.Empty<PdfLinkAnnotation>());

    /// <summary>
    /// 指定された範囲矩形と交差する文字群をインデックス順に抽出し、文字列として取得します。
    /// </summary>
    public (string SelectedText, IReadOnlyList<PdfTextCharacter> SelectedCharacters) GetTextInRect(Rect selectionRect)
    {
        if (Characters.Count == 0 || selectionRect.IsEmpty || selectionRect.Width <= 0 || selectionRect.Height <= 0)
        {
            return (string.Empty, Array.Empty<PdfTextCharacter>());
        }

        var selected = new List<PdfTextCharacter>();
        foreach (var c in Characters)
        {
            var charRect = c.BoundingBox;
            if (!selectionRect.IntersectsWith(charRect)) continue;

            // 文字の中心点が含まれているか、または交差幅が文字幅の半分以上の場合に選択対象とする
            var center = new Point(charRect.Left + charRect.Width * 0.5, charRect.Top + charRect.Height * 0.5);
            var intersect = Rect.Intersect(selectionRect, charRect);
            if (selectionRect.Contains(center) || intersect.Width >= charRect.Width * 0.5)
            {
                selected.Add(c);
            }
        }

        // 微小な選択矩形ですべて除外された場合のフォールバック（1文字内部のドラッグ等）
        if (selected.Count == 0)
        {
            foreach (var c in Characters)
            {
                if (selectionRect.IntersectsWith(c.BoundingBox))
                {
                    selected.Add(c);
                }
            }
        }

        if (selected.Count == 0)
        {
            return (string.Empty, Array.Empty<PdfTextCharacter>());
        }

        var sb = new StringBuilder(selected.Count);
        foreach (var c in selected)
        {
            sb.Append(c.Character);
        }

        return (sb.ToString(), selected);
    }
}
