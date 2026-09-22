using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PDFBinder.App.Controls;

/// <summary>
/// ドラッグ操作中にマウスカーソルに追従する浮遊サムネイルを描画するアドーナー
/// </summary>
public class DragAdorner : Adorner
{
    private readonly UIElement _child;
    private Point _currentPosition;
    private readonly double _offsetX;
    private readonly double _offsetY;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="adornedElement">装飾対象の親要素</param>
    /// <param name="child">描画する浮遊UI要素</param>
    /// <param name="offset">カーソルからの相対オフセット</param>
    public DragAdorner(UIElement adornedElement, UIElement child, Point offset)
        : base(adornedElement)
    {
        _child = child ?? throw new ArgumentNullException(nameof(child));
        _offsetX = offset.X;
        _offsetY = offset.Y;
        AddVisualChild(_child);
        IsHitTestVisible = false;
    }

    /// <summary>
    /// カーソル位置を更新します。
    /// </summary>
    /// <param name="position">親要素に対するカーソルの現在座標</param>
    public void UpdatePosition(Point position)
    {
        _currentPosition = position;
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index)
    {
        if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
        return _child;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _child.Measure(constraint);
        return _child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var location = new Point(_currentPosition.X + _offsetX, _currentPosition.Y + _offsetY);
        _child.Arrange(new Rect(location, _child.DesiredSize));
        return finalSize;
    }
}
