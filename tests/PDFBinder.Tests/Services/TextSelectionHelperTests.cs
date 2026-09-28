using System.Windows;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests.Services;

/// <summary>
/// <see cref="TextSelectionHelper"/> の行単位・ストリーム順テキスト選択に関する単体テスト
/// </summary>
public class TextSelectionHelperTests
{
    /// <summary>
    /// テスト用の文字モデル群を作成するヘルパー
    /// </summary>
    private static List<PdfTextCharacter> CreateLineCharacters(
        string text,
        double startX,
        double y,
        double charWidth = 10.0,
        double charHeight = 15.0,
        int startIndex = 0)
    {
        var list = new List<PdfTextCharacter>();
        for (int i = 0; i < text.Length; i++)
        {
            var rect = new Rect(startX + (i * charWidth), y, charWidth, charHeight);
            list.Add(new PdfTextCharacter(text[i], rect, startIndex + i));
        }
        return list;
    }

    [Fact]
    public void SelectText_NullOrEmptyCharacters_ReturnsEmpty()
    {
        // Act
        var result1 = TextSelectionHelper.SelectText(null!, new Point(0, 0), new Point(100, 100));
        var result2 = TextSelectionHelper.SelectText(Array.Empty<PdfTextCharacter>(), new Point(0, 0), new Point(100, 100));

        // Assert
        Assert.Equal(TextSelectionResult.Empty, result1);
        Assert.Equal(TextSelectionResult.Empty, result2);
    }

    [Fact]
    public void SelectText_SingleLine_ForwardDrag_SelectsExpectedRange()
    {
        // Arrange: 1行の文字列 "Hello, World!" (Y: 100〜115)
        var chars = CreateLineCharacters("Hello, World!", startX: 50, y: 100, charWidth: 10, charHeight: 15);

        // Act: 'e'(インデックス1, X: 65) から 'W'(インデックス7, X: 125) までドラッグ
        var startPoint = new Point(65, 105);
        var endPoint = new Point(125, 105);
        var result = TextSelectionHelper.SelectText(chars, startPoint, endPoint);

        // Assert
        Assert.Equal("ello, W", result.SelectedText);
        Assert.Equal(7, result.SelectedCharacters.Count);
        Assert.Single(result.HighlightRects);
        // ハイライト矩形の幅が文字群全体の幅と一致すること
        Assert.Equal(70.0, result.HighlightRects[0].Width);
    }

    [Fact]
    public void SelectText_SingleLine_BackwardDrag_SelectsSameRange()
    {
        // Arrange: 1行の文字列
        var chars = CreateLineCharacters("ABCDEF", startX: 10, y: 50, charWidth: 10, charHeight: 15);

        // Act: 'D'(X: 45) から 'B'(X: 25) へ逆方向ドラッグ
        var startPoint = new Point(45, 55);
        var endPoint = new Point(25, 55);
        var result = TextSelectionHelper.SelectText(chars, startPoint, endPoint);

        // Assert: 順序が正規化されて "BCD" が選択される
        Assert.Equal("BCD", result.SelectedText);
        Assert.Equal(3, result.SelectedCharacters.Count);
        Assert.Single(result.HighlightRects);
    }

    [Fact]
    public void SelectText_MultipleLines_InsertsNewLineAtLineBreak()
    {
        // Arrange: 2行の文章
        // 行1: "こんにちは、" (Y: 50〜65)
        // 行2: "世界！"       (Y: 80〜95)
        var line1 = CreateLineCharacters("こんにちは、", startX: 20, y: 50, charWidth: 12, charHeight: 15, startIndex: 0);
        var line2 = CreateLineCharacters("世界！", startX: 20, y: 80, charWidth: 12, charHeight: 15, startIndex: line1.Count);
        var allChars = line1.Concat(line2).ToList();

        // Act: 1行目の「に」(X: 45, Y: 55) から 2行目の「界」(X: 35, Y: 85) までドラッグ
        var startPoint = new Point(45, 55);
        var endPoint = new Point(35, 85);
        var result = TextSelectionHelper.SelectText(allChars, startPoint, endPoint);

        // Assert: 行の変わり目に改行コードが挿入されること
        string expected = $"にちは、{Environment.NewLine}世界";
        Assert.Equal(expected, result.SelectedText);
        Assert.Equal(2, result.HighlightRects.Count);
    }

    [Fact]
    public void SelectText_PunctuationAndSymbols_PreservedWithoutLoss()
    {
        // Arrange: 読点「、」や句点「。」、記号を含む文字列
        var chars = CreateLineCharacters("テスト、完了。", startX: 10, y: 30, charWidth: 12, charHeight: 15);

        // Act: 'テ'（X: 16）から'。'（X: 88）まで全文字を選択
        var startPoint = new Point(16, 35);
        var endPoint = new Point(88, 35);
        var result = TextSelectionHelper.SelectText(chars, startPoint, endPoint);

        // Assert: 句読点がすべて欠落せず含まれていること
        Assert.Equal("テスト、完了。", result.SelectedText);
        Assert.Equal(7, result.SelectedCharacters.Count);
    }

    [Fact]
    public void FindClosestCharacter_MarginSnap_SnapsToLineStartAndEnd()
    {
        // Arrange: 1行の文字列 "PDF" (X: 100〜130)
        var chars = CreateLineCharacters("PDF", startX: 100, y: 50, charWidth: 10, charHeight: 15);

        // Act 1: 行の左外側余白 (X: 20, Y: 55)
        var leftChar = TextSelectionHelper.FindClosestCharacter(chars, new Point(20, 55));

        // Act 2: 行の右外側余白 (X: 200, Y: 55)
        var rightChar = TextSelectionHelper.FindClosestCharacter(chars, new Point(200, 55));

        // Assert: 行頭 'P' および 行末 'F' にスナップすること
        Assert.NotNull(leftChar);
        Assert.Equal('P', leftChar.Character);
        Assert.NotNull(rightChar);
        Assert.Equal('F', rightChar.Character);
    }

    [Fact]
    public void FindClosestCharacter_VerticalMargin_SnapsToNearestLine()
    {
        // Arrange: 2行の文字列
        var line1 = CreateLineCharacters("Line1", startX: 10, y: 20, charWidth: 10, charHeight: 15, startIndex: 0);
        var line2 = CreateLineCharacters("Line2", startX: 10, y: 60, charWidth: 10, charHeight: 15, startIndex: 5);
        var allChars = line1.Concat(line2).ToList();

        // Act 1: ページ上部余白 (Y: 5) -> line1 にスナップ
        var topChar = TextSelectionHelper.FindClosestCharacter(allChars, new Point(25, 5));

        // Act 2: ページ下部余白 (Y: 100) -> line2 にスナップ
        var bottomChar = TextSelectionHelper.FindClosestCharacter(allChars, new Point(25, 100));

        // Assert
        Assert.NotNull(topChar);
        Assert.Equal('i', topChar.Character);
        Assert.Equal(0, topChar.CharacterIndex / 5); // line1 の文字

        Assert.NotNull(bottomChar);
        Assert.Equal('i', bottomChar.Character);
        Assert.Equal(1, bottomChar.CharacterIndex / 5); // line2 の文字
    }

    [Fact]
    public void CalculateHighlightRectangles_MergesCharactersOnSameLine()
    {
        // Arrange: 1行に5文字
        var chars = CreateLineCharacters("ABCDE", startX: 50, y: 100, charWidth: 10, charHeight: 20);

        // Act
        var rects = TextSelectionHelper.CalculateHighlightRectangles(chars);

        // Assert: 同一行なので1つの矩形にマージされる
        Assert.Single(rects);
        var rect = rects[0];
        Assert.Equal(50.0, rect.Left);
        Assert.Equal(100.0, rect.Top);
        Assert.Equal(50.0, rect.Width); // 5文字 * 10
        Assert.Equal(20.0, rect.Height);
    }

    [Fact]
    public void PageInteractiveData_GetTextInRange_ReturnsValidSelectionResult()
    {
        // Arrange
        var chars = CreateLineCharacters("Binder", startX: 10, y: 20, charWidth: 10, charHeight: 15);
        var data = new PageInteractiveData(chars, Array.Empty<PdfLinkAnnotation>());

        // Act
        var result = data.GetTextInRange(new Point(15, 25), new Point(35, 25));

        // Assert
        Assert.Equal("Bin", result.SelectedText);
        Assert.Equal(3, result.SelectedCharacters.Count);
        Assert.Single(result.HighlightRects);
    }
}
