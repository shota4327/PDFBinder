using System.Text;
using System.Windows;
using PDFBinder.Core.Models;

namespace PDFBinder.Core.Services;

/// <summary>
/// テキスト選択結果を表すデータモデル
/// </summary>
/// <param name="SelectedText">改行コードを含む選択文字列</param>
/// <param name="SelectedCharacters">選択された文字情報のリスト</param>
/// <param name="HighlightRects">行単位に統合されたハイライト描画用矩形リスト</param>
public record TextSelectionResult(
    string SelectedText,
    IReadOnlyList<PdfTextCharacter> SelectedCharacters,
    IReadOnlyList<Rect> HighlightRects
)
{
    /// <summary>空の選択結果</summary>
    public static TextSelectionResult Empty { get; } = new(
        string.Empty,
        Array.Empty<PdfTextCharacter>(),
        Array.Empty<Rect>()
    );
}

/// <summary>
/// PDFページのテキスト選択、行スナップ、改行挿入、およびハイライト矩形計算を担うヘルパークラス
/// </summary>
public static class TextSelectionHelper
{
    /// <summary>
    /// 指定された開始座標と終了座標に基づいて文字範囲を選択し、テキストとハイライト矩形を算出します。
    /// </summary>
    public static TextSelectionResult SelectText(
        IReadOnlyList<PdfTextCharacter> characters,
        Point startPoint,
        Point endPoint)
    {
        if (characters == null || characters.Count == 0)
        {
            return TextSelectionResult.Empty;
        }

        var startChar = FindClosestCharacter(characters, startPoint);
        var endChar = FindClosestCharacter(characters, endPoint);

        if (startChar == null || endChar == null)
        {
            return TextSelectionResult.Empty;
        }

        int minIndex = Math.Min(startChar.CharacterIndex, endChar.CharacterIndex);
        int maxIndex = Math.Max(startChar.CharacterIndex, endChar.CharacterIndex);

        var selected = new List<PdfTextCharacter>();
        foreach (var c in characters)
        {
            if (c.CharacterIndex >= minIndex && c.CharacterIndex <= maxIndex)
            {
                selected.Add(c);
            }
        }

        if (selected.Count == 0)
        {
            return TextSelectionResult.Empty;
        }

        string text = BuildSelectedText(selected);
        var highlightRects = CalculateHighlightRectangles(selected);

        return new TextSelectionResult(text, selected, highlightRects);
    }

    /// <summary>
    /// 指定座標に最も近い文字を行スナップおよび余白判定を考慮して特定します。
    /// </summary>
    public static PdfTextCharacter? FindClosestCharacter(
        IReadOnlyList<PdfTextCharacter> characters,
        Point point)
    {
        if (characters == null || characters.Count == 0)
        {
            return null;
        }

        // ステップ1: Y座標が含まれる行候補の文字群を収集
        var lineCandidates = FindLineCandidates(characters, point.Y);
        if (lineCandidates.Count == 0)
        {
            // 上下余白または行間の場合、垂直距離が最も近い行候補を収集
            lineCandidates = FindNearestVerticalLineCandidates(characters, point.Y);
        }

        if (lineCandidates.Count == 0)
        {
            return characters[0];
        }

        // ステップ2: 行候補内でX座標に基づいてスナップ判定
        return SnapToCharacterInLine(lineCandidates, point.X);
    }

    /// <summary>
    /// Y座標が範囲内に含まれる行候補の文字群を抽出します。
    /// </summary>
    private static List<PdfTextCharacter> FindLineCandidates(
        IReadOnlyList<PdfTextCharacter> characters,
        double targetY)
    {
        var candidates = new List<PdfTextCharacter>();
        foreach (var c in characters)
        {
            var box = c.BoundingBox;
            double verticalMargin = box.Height * 0.2;
            if (targetY >= box.Top - verticalMargin && targetY <= box.Bottom + verticalMargin)
            {
                candidates.Add(c);
            }
        }
        return candidates;
    }

    /// <summary>
    /// 垂直距離（Y座標）が最も近い行候補の文字群を抽出します。
    /// </summary>
    private static List<PdfTextCharacter> FindNearestVerticalLineCandidates(
        IReadOnlyList<PdfTextCharacter> characters,
        double targetY)
    {
        double minDistance = double.MaxValue;
        PdfTextCharacter? nearestChar = null;

        foreach (var c in characters)
        {
            var box = c.BoundingBox;
            double dist = targetY < box.Top ? box.Top - targetY :
                          targetY > box.Bottom ? targetY - box.Bottom : 0.0;

            if (dist < minDistance)
            {
                minDistance = dist;
                nearestChar = c;
            }
        }

        if (nearestChar == null) return new List<PdfTextCharacter>();

        // 最も近い文字と同一行にある文字群を収集
        var result = new List<PdfTextCharacter>();
        foreach (var c in characters)
        {
            if (!IsDifferentLine(nearestChar, c))
            {
                result.Add(c);
            }
        }
        return result;
    }

    /// <summary>
    /// 行候補の文字群から、X座標に基づいて文字にスナップ（余白なら行頭・行末）します。
    /// </summary>
    private static PdfTextCharacter SnapToCharacterInLine(
        List<PdfTextCharacter> lineCharacters,
        double targetX)
    {
        PdfTextCharacter leftmost = lineCharacters[0];
        PdfTextCharacter rightmost = lineCharacters[0];

        foreach (var c in lineCharacters)
        {
            if (c.BoundingBox.Left < leftmost.BoundingBox.Left) leftmost = c;
            if (c.BoundingBox.Right > rightmost.BoundingBox.Right) rightmost = c;
        }

        // 行頭より左側の余白なら行頭文字にスナップ
        if (targetX <= leftmost.BoundingBox.Left)
        {
            return leftmost;
        }

        // 行末より右側の余白なら行末文字にスナップ
        if (targetX >= rightmost.BoundingBox.Right)
        {
            return rightmost;
        }

        // 行内の場合、中心X座標との水平距離が最も近い文字を選択
        PdfTextCharacter bestChar = lineCharacters[0];
        double minDistance = double.MaxValue;

        foreach (var c in lineCharacters)
        {
            double centerX = c.BoundingBox.Left + c.BoundingBox.Width * 0.5;
            double dist = Math.Abs(centerX - targetX);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestChar = c;
            }
        }

        return bestChar;
    }

    /// <summary>
    /// 2つの文字が異なる行に属するか判定します。
    /// </summary>
    public static bool IsDifferentLine(PdfTextCharacter a, PdfTextCharacter b)
    {
        var boxA = a.BoundingBox;
        var boxB = b.BoundingBox;

        double overlapTop = Math.Max(boxA.Top, boxB.Top);
        double overlapBottom = Math.Min(boxA.Bottom, boxB.Bottom);
        double overlap = overlapBottom - overlapTop;
        double minHeight = Math.Min(boxA.Height, boxB.Height);

        // 垂直方向の重なりが高さの35%未満なら別行と判定
        if (minHeight > 0 && overlap < minHeight * 0.35)
        {
            return true;
        }

        // 中心Y座標の差が高さの半分以上離れている場合も別行と判定
        double centerYA = boxA.Top + boxA.Height * 0.5;
        double centerYB = boxB.Top + boxB.Height * 0.5;
        if (minHeight > 0 && Math.Abs(centerYA - centerYB) > minHeight * 0.5)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 選択された文字群から、行の変わり目に改行を挿入して文字列を生成します。
    /// </summary>
    public static string BuildSelectedText(IReadOnlyList<PdfTextCharacter> selectedCharacters)
    {
        if (selectedCharacters == null || selectedCharacters.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(selectedCharacters.Count + 16);
        for (int i = 0; i < selectedCharacters.Count; i++)
        {
            var current = selectedCharacters[i];
            sb.Append(current.Character);

            if (i + 1 < selectedCharacters.Count)
            {
                var next = selectedCharacters[i + 1];
                if (IsDifferentLine(current, next))
                {
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 選択文字群から、行ごとに一体化したハイライト矩形リストを計算します。
    /// </summary>
    public static IReadOnlyList<Rect> CalculateHighlightRectangles(
        IReadOnlyList<PdfTextCharacter> selectedCharacters)
    {
        var rects = new List<Rect>();
        if (selectedCharacters == null || selectedCharacters.Count == 0)
        {
            return rects;
        }

        Rect currentLineRect = selectedCharacters[0].BoundingBox;
        for (int i = 1; i < selectedCharacters.Count; i++)
        {
            var prev = selectedCharacters[i - 1];
            var curr = selectedCharacters[i];

            if (IsDifferentLine(prev, curr))
            {
                rects.Add(currentLineRect);
                currentLineRect = curr.BoundingBox;
            }
            else
            {
                currentLineRect.Union(curr.BoundingBox);
            }
        }

        rects.Add(currentLineRect);
        return rects;
    }
}
