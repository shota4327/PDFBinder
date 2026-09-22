using System.Windows.Ink;
using System.Windows.Input;
using PDFBinder.Core.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// <see cref="StrokeSplitHelper"/> の単体テストクラス（Issue #137）
/// </summary>
public class StrokeSplitHelperTests
{
    [Fact]
    public void SplitStrokes_NullOrEmpty_ReturnsEmptyCollections()
    {
        var (f1, s1) = StrokeSplitHelper.SplitStrokes(null, true, 100);
        Assert.Empty(f1);
        Assert.Empty(s1);

        var (f2, s2) = StrokeSplitHelper.SplitStrokes(new StrokeCollection(), true, 100);
        Assert.Empty(f2);
        Assert.Empty(s2);

        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(10, 10), new StylusPoint(20, 20) });
        var (f3, s3) = StrokeSplitHelper.SplitStrokes(new StrokeCollection { stroke }, true, 0);
        Assert.Empty(f3);
        Assert.Empty(s3);
    }

    [Fact]
    public void SplitStrokes_SingleSide_LeftOnly_SplitsHorizontally()
    {
        // 左右分割、中央X=100。ストロークはX=10..50
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(10, 20), new StylusPoint(50, 30) });
        var collection = new StrokeCollection { stroke };

        var (left, right) = StrokeSplitHelper.SplitStrokes(collection, true, 100);

        Assert.Single(left);
        Assert.Empty(right);
        Assert.Equal(10, left[0].StylusPoints[0].X);
        Assert.Equal(50, left[0].StylusPoints[1].X);
    }

    [Fact]
    public void SplitStrokes_SingleSide_RightOnly_SplitsHorizontallyAndShifts()
    {
        // 左右分割、中央X=100。ストロークはX=120..150 -> 右ページでは X=20..50 にシフト
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(120, 20), new StylusPoint(150, 30) });
        var collection = new StrokeCollection { stroke };

        var (left, right) = StrokeSplitHelper.SplitStrokes(collection, true, 100);

        Assert.Empty(left);
        Assert.Single(right);
        Assert.Equal(20, right[0].StylusPoints[0].X);
        Assert.Equal(50, right[0].StylusPoints[1].X);
    }

    [Fact]
    public void SplitStrokes_CrossingBoundary_HorizontalSplit_CutsIntoBothParts()
    {
        // 左右分割、中央X=100。始点(50, 10)から終点(150, 30)へ跨ぐストローク
        // 交点: X=100, Y=20 (t=0.5)
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(50, 10), new StylusPoint(150, 30) });
        var collection = new StrokeCollection { stroke };

        var (left, right) = StrokeSplitHelper.SplitStrokes(collection, true, 100);

        Assert.Single(left);
        Assert.Single(right);

        // 左側: (50, 10) -> (100, 20)
        Assert.Equal(2, left[0].StylusPoints.Count);
        Assert.Equal(50, left[0].StylusPoints[0].X);
        Assert.Equal(10, left[0].StylusPoints[0].Y);
        Assert.Equal(100, left[0].StylusPoints[1].X);
        Assert.Equal(20, left[0].StylusPoints[1].Y);

        // 右側: (100-100, 20) -> (150-100, 30) => (0, 20) -> (50, 30)
        Assert.Equal(2, right[0].StylusPoints.Count);
        Assert.Equal(0, right[0].StylusPoints[0].X);
        Assert.Equal(20, right[0].StylusPoints[0].Y);
        Assert.Equal(50, right[0].StylusPoints[1].X);
        Assert.Equal(30, right[0].StylusPoints[1].Y);
    }

    [Fact]
    public void SplitStrokes_CrossingBoundary_VerticalSplit_CutsIntoBothParts()
    {
        // 上下分割、中央Y=200。始点(30, 100)から終点(50, 300)へ跨ぐストローク
        // 交点: Y=200, X=40 (t=0.5)
        var stroke = new Stroke(new StylusPointCollection { new StylusPoint(30, 100), new StylusPoint(50, 300) });
        var collection = new StrokeCollection { stroke };

        var (top, bottom) = StrokeSplitHelper.SplitStrokes(collection, false, 200);

        Assert.Single(top);
        Assert.Single(bottom);

        // 上側: (30, 100) -> (40, 200)
        Assert.Equal(30, top[0].StylusPoints[0].X);
        Assert.Equal(100, top[0].StylusPoints[0].Y);
        Assert.Equal(40, top[0].StylusPoints[1].X);
        Assert.Equal(200, top[0].StylusPoints[1].Y);

        // 下側: (40, 200-200) -> (50, 300-200) => (40, 0) -> (50, 100)
        Assert.Equal(40, bottom[0].StylusPoints[0].X);
        Assert.Equal(0, bottom[0].StylusPoints[0].Y);
        Assert.Equal(50, bottom[0].StylusPoints[1].X);
        Assert.Equal(100, bottom[0].StylusPoints[1].Y);
    }
}
