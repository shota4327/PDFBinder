using System.Windows;

namespace PDFBinder.Core.Models;

/// <summary>
/// PDFページ内の抽出された1文字の情報を表すモデル
/// </summary>
/// <param name="Character">文字</param>
/// <param name="BoundingBox">WPF表示座標系（Dip単位、左上原点）における文字の矩形領域</param>
/// <param name="CharacterIndex">ページ内での通しインデックス</param>
public record PdfTextCharacter(
    char Character,
    Rect BoundingBox,
    int CharacterIndex
);
