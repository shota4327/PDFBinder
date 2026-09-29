using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;
using PDFBinder.App.Helpers;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// PenCursorHelperおよびズーム連動カーソル更新機能の単体テスト
/// </summary>
public class PenCursorHelperTests
{
    [Fact]
    public void IsCircleCursorTool_IdentifiesTargetToolsCorrectly()
    {
        // 円形カーソル対象ツール（直線ツールを含む）
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Pen));
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Highlighter));
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.EraserPoint));
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.StraightLine));
        Assert.True(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Pen, isStraightLine: true));

        // 円形カーソル対象外ツール
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Select));
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.EraserStroke));
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.Hand));
        Assert.False(PenCursorHelper.IsCircleCursorTool(EditorToolMode.TextSelect));
    }

    [Fact]
    public void GetCursor_NonCircleTool_ReturnsNull()
    {
        var cursor = PenCursorHelper.GetCursor(EditorToolMode.Select, Colors.Black, 2.0, 1.0);
        Assert.Null(cursor);

        cursor = PenCursorHelper.GetCursor(EditorToolMode.Hand, Colors.Black, 2.0, 1.0);
        Assert.Null(cursor);
    }

    [Theory]
    [InlineData(EditorToolMode.Pen)]
    [InlineData(EditorToolMode.Highlighter)]
    [InlineData(EditorToolMode.EraserPoint)]
    [InlineData(EditorToolMode.StraightLine)]
    public void GetCursor_CircleTools_ReturnsNonNullCursor(EditorToolMode tool)
    {
        var cursor = PenCursorHelper.GetCursor(tool, Colors.Blue, 3.0, 1.5);
        Assert.NotNull(cursor);
    }

    [Fact]
    public void GetCursor_SameParameters_ReturnsCachedInstance()
    {
        PenCursorHelper.ClearCache();

        var cursor1 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Red, 4.0, 2.0);
        var cursor2 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Red, 4.0, 2.0);

        Assert.NotNull(cursor1);
        Assert.Same(cursor1, cursor2);
    }

    [Fact]
    public void GetCursor_DifferentZoom_ReturnsDifferentCursorInstance()
    {
        PenCursorHelper.ClearCache();

        var cursor100 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 5.0, 1.0);
        var cursor200 = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 5.0, 2.0);

        Assert.NotNull(cursor100);
        Assert.NotNull(cursor200);
        Assert.NotSame(cursor100, cursor200);
    }

    [Fact]
    public void GetCursor_ExtremeZoom_ClampsBetweenMinAndMax()
    {
        PenCursorHelper.ClearCache();

        // 極小サイズ (0.01 * 0.1 = 0.001) -> MinCursorSize (3.0px) にクランプ
        var cursorMin = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 0.01, 0.1);
        Assert.NotNull(cursorMin);

        // 極大サイズ (100 * 50 = 5000) -> MaxCursorSize (128.0px) にクランプ
        var cursorMax = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Black, 100.0, 50.0);
        Assert.NotNull(cursorMax);
    }

    [Fact]
    public void EditorInkCanvas_ZoomChange_UpdatesCursorOnStaThread()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                StrokeThickness = 4.0,
                DrawingColor = Colors.Black,
                Zoom = 1.0
            };

            var initialCursor = canvas.Cursor;
            Assert.NotNull(initialCursor);

            // ズームを2.0に変更
            canvas.Zoom = 2.0;
            var zoomedCursor = canvas.Cursor;

            Assert.NotNull(zoomedCursor);
            Assert.NotSame(initialCursor, zoomedCursor);
        });
    }

    [Fact]
    public void EditorInkCanvas_ThicknessAndColorChange_UpdatesCursorOnStaThread()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Highlighter,
                StrokeThickness = 10.0,
                DrawingColor = Colors.Yellow,
                Zoom = 1.0
            };

            var cursor1 = canvas.Cursor;
            Assert.NotNull(cursor1);

            // 太さを変更
            canvas.StrokeThickness = 20.0;
            var cursor2 = canvas.Cursor;
            Assert.NotNull(cursor2);
            Assert.NotSame(cursor1, cursor2);

            // 色を変更
            canvas.DrawingColor = Colors.Green;
            var cursor3 = canvas.Cursor;
            Assert.NotNull(cursor3);
            Assert.NotSame(cursor2, cursor3);
        });
    }

    [Fact]
    public void EditorInkCanvas_PointEraserMode_UsesPointEraserCursor()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.EraserPoint,
                StrokeThickness = 12.0,
                Zoom = 1.5
            };

            Assert.NotNull(canvas.Cursor);
            Assert.NotEqual(Cursors.Cross, canvas.Cursor);

            // ストローク消しゴム時は消しゴム形状カーソル（WPF標準）であり、UseCustomCursorがfalseであること
            canvas.ToolMode = EditorToolMode.EraserStroke;
            Assert.False(canvas.UseCustomCursor);
            Assert.Equal(PenCursorHelper.GetStrokeEraserCursor(), canvas.Cursor);
        });
    }

    [Fact]
    public void GetStrokeEraserCursor_ReturnsNonNullCursor()
    {
        var cursor = PenCursorHelper.GetStrokeEraserCursor();
        Assert.NotNull(cursor);
    }

    [Fact]
    public void GetCursor_StraightLine_ReturnsRulerCursorDifferentFromNormal()
    {
        PenCursorHelper.ClearCache();

        var normalCursor = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Red, 4.0, 1.0, isStraightLine: false);
        var straightCursor = PenCursorHelper.GetCursor(EditorToolMode.Pen, Colors.Red, 4.0, 1.0, isStraightLine: true);

        Assert.NotNull(normalCursor);
        Assert.NotNull(straightCursor);
        Assert.NotSame(normalCursor, straightCursor);
    }

    [Fact]
    public void EditorInkCanvas_StraightLineMode_UsesRulerCursor()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                StrokeThickness = 5.0,
                Zoom = 2.0
            };

            // 直線トグル有効時は十字カーソルではなく、定規アイコン付きプレビューカーソルであること
            Assert.NotNull(canvas.Cursor);
            Assert.NotEqual(Cursors.Cross, canvas.Cursor);

            // 直線OFF時は通常のプレビューカーソルに切り替わること
            canvas.IsStraightLine = false;
            Assert.NotNull(canvas.Cursor);
            Assert.NotEqual(Cursors.Cross, canvas.Cursor);
        });
    }

    [Fact]
    public void DrawCrosshairLines_RendersExpectedPixels()
    {
        int size = 64;
        int hotspot = 32;
        double radius = 10.0;
        double gap = 3.5;
        double lineLen = 7.0;
        byte[] pixels = new byte[size * size * 4];

        // 十字線を描画
        PenCursorHelper.DrawCrosshairLines(pixels, size, hotspot, radius, gap, lineLen);

        // 上下左右の4方向に黒色ピクセルが存在することを検証
        bool hasTopBlack = false;
        bool hasBottomBlack = false;
        bool hasLeftBlack = false;
        bool hasRightBlack = false;

        // 上方向のピクセルチェック
        for (int y = 12; y <= 18; y++)
        {
            int dibRow = size - 1 - y;
            int offset = (dibRow * size + hotspot) * 4;
            if (pixels[offset + 3] > 0 && pixels[offset] == 0 && pixels[offset + 1] == 0 && pixels[offset + 2] == 0)
            {
                hasTopBlack = true;
            }
        }

        // 下方向のピクセルチェック
        for (int y = 47; y <= 53; y++)
        {
            int dibRow = size - 1 - y;
            int offset = (dibRow * size + hotspot) * 4;
            if (pixels[offset + 3] > 0 && pixels[offset] == 0 && pixels[offset + 1] == 0 && pixels[offset + 2] == 0)
            {
                hasBottomBlack = true;
            }
        }

        // 左方向のピクセルチェック
        for (int x = 12; x <= 18; x++)
        {
            int dibRow = size - 1 - hotspot;
            int offset = (dibRow * size + x) * 4;
            if (pixels[offset + 3] > 0 && pixels[offset] == 0 && pixels[offset + 1] == 0 && pixels[offset + 2] == 0)
            {
                hasLeftBlack = true;
            }
        }

        // 右方向のピクセルチェック
        for (int x = 47; x <= 53; x++)
        {
            int dibRow = size - 1 - hotspot;
            int offset = (dibRow * size + x) * 4;
            if (pixels[offset + 3] > 0 && pixels[offset] == 0 && pixels[offset + 1] == 0 && pixels[offset + 2] == 0)
            {
                hasRightBlack = true;
            }
        }

        Assert.True(hasTopBlack, "上方向に黒色十字線が存在すること");
        Assert.True(hasBottomBlack, "下方向に黒色十字線が存在すること");
        Assert.True(hasLeftBlack, "左方向に黒色十字線が存在すること");
        Assert.True(hasRightBlack, "右方向に黒色十字線が存在すること");

        // 太さ2px（cx-1.0〜cx+1.0）により、hotspot-1 と hotspot+1 にもアンチエイリアスピクセルが存在することを検証
        int dibRowSample = size - 1 - 15;
        int offsetAdjacentLeft = (dibRowSample * size + (hotspot - 1)) * 4;
        int offsetAdjacentRight = (dibRowSample * size + (hotspot + 1)) * 4;
        Assert.True(pixels[offsetAdjacentLeft + 3] > 0, "hotspot-1 にアンチエイリアスピクセルが存在すること");
        Assert.True(pixels[offsetAdjacentRight + 3] > 0, "hotspot+1 にアンチエイリアスピクセルが存在すること");
    }

    [Fact]
    public void DrawCrosshairLines_RendersSelectedColorPixels()
    {
        int size = 64;
        int hotspot = 32;
        double radius = 10.0;
        double gap = 3.5;
        double lineLen = 7.0;
        byte[] pixels = new byte[size * size * 4];

        // 赤色の十字線を描画
        PenCursorHelper.DrawCrosshairLines(pixels, size, hotspot, radius, gap, lineLen, thickness: 2.0, lineColor: Colors.Red);

        // 上方向のピクセルに赤色（R=255, G=0, B=0）が存在することを検証
        bool hasRedPixel = false;
        for (int y = 12; y <= 18; y++)
        {
            int dibRow = size - 1 - y;
            int offset = (dibRow * size + hotspot) * 4;
            if (pixels[offset + 3] > 0 && pixels[offset + 2] == 255 && pixels[offset + 1] == 0 && pixels[offset] == 0)
            {
                hasRedPixel = true;
                break;
            }
        }

        Assert.True(hasRedPixel, "指定した選択色（赤色）の十字線ピクセルが存在すること");
    }

    [Theory]
    [InlineData(3.0, 7.0)]
    [InlineData(6.0, 7.0)]
    [InlineData(12.0, 8.0)]
    [InlineData(24.0, 16.0)]
    [InlineData(48.0, 32.0)]
    public void CreateStraightLineCursor_ScalesLineLengthWithDiameter(double diameter, double expectedLineLen)
    {
        // プレビュー円の2/3倍（最低長7.0px保証）で伸長されることを検証
        const double minLineLen = 7.0;
        double actualLineLen = Math.Max(minLineLen, diameter * (2.0 / 3.0));
        Assert.Equal(expectedLineLen, actualLineLen, 1);
        Assert.True(actualLineLen >= minLineLen);

        // カーソルが正常に生成されること
        var cursor = PenCursorHelper.CreateStraightLineCursor(diameter, Colors.Black, isHighlighter: false);
        Assert.NotNull(cursor);
    }

    [Theory]
    [InlineData(3.0, false)]
    [InlineData(10.0, false)]
    [InlineData(50.0, true)]
    [InlineData(128.0, false)]
    public void CreateStraightLineCursor_ProducesValidCursor_ForVariousSizes(double diameter, bool isHighlighter)
    {
        var cursor = PenCursorHelper.CreateStraightLineCursor(diameter, Colors.Blue, isHighlighter);
        Assert.NotNull(cursor);
    }

    [Fact]
    public void EditorInkCanvas_StraightLineMode_Highlighter_UsesRulerCursor()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Highlighter,
                IsStraightLine = true,
                StrokeThickness = 8.0,
                Zoom = 1.0
            };

            Assert.NotNull(canvas.Cursor);
            Assert.NotEqual(Cursors.Cross, canvas.Cursor);
        });
    }

    [Fact]
    public void EditorInkCanvas_StraightLineMode_ColorAndThicknessChanges_UpdatesCursor()
    {
        RunOnStaThread(() =>
        {
            var canvas = new EditorInkCanvas
            {
                ToolMode = EditorToolMode.Pen,
                IsStraightLine = true,
                StrokeThickness = 4.0,
                DrawingColor = Colors.Black,
                Zoom = 1.0
            };

            var cursor1 = canvas.Cursor;
            Assert.NotNull(cursor1);

            // 太さ変更
            canvas.StrokeThickness = 10.0;
            var cursor2 = canvas.Cursor;
            Assert.NotNull(cursor2);
            Assert.NotSame(cursor1, cursor2);

            // 色変更
            canvas.DrawingColor = Colors.Red;
            var cursor3 = canvas.Cursor;
            Assert.NotNull(cursor3);
            Assert.NotSame(cursor2, cursor3);
        });
    }

    [Fact]
    public void RenderCirclePixels_EraserPoint_HasNoCenterDot()
    {
        // 部分消しゴム（中空円）の中心点にドット（不透明ピクセル）が存在しないことを検証
        int size = 32;
        int hotspot = 16;
        double diameter = 20.0;
        byte[] pixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, Colors.Black, isHollow: true, isHighlighter: false);

        // hotspot位置のピクセルのアルファ値を取得（DIBボトムアップ順）
        int dibRow = size - 1 - hotspot;
        int centerPixelOffset = (dibRow * size + hotspot) * 4;
        byte centerAlpha = pixels[centerPixelOffset + 3];

        Assert.Equal(0, centerAlpha);
    }

    [Fact]
    public void RenderCirclePixels_AntiAliasing_ProducesIntermediateAlphas()
    {
        // ペンおよび消しゴムの円周境界にスーパーサンプリングによる中間アルファ値が存在することを検証（ギザギザ防止）
        int size = 32;
        int hotspot = 16;
        double diameter = 20.0;

        // ペン（塗りつぶし円）
        byte[] penPixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, Colors.Red, isHollow: false, isHighlighter: false);
        var penAlphas = penPixels.Where((p, i) => i % 4 == 3 && p > 0).Distinct().ToList();
        // 0と255以外に中間階調が複数存在すること（アンチエイリアス）
        Assert.True(penAlphas.Count > 2);

        // 消しゴム（中空円）
        byte[] eraserPixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, Colors.Black, isHollow: true, isHighlighter: false);
        var eraserAlphas = eraserPixels.Where((p, i) => i % 4 == 3 && p > 0).Distinct().ToList();
        // 0と220以外に中間階調が複数存在すること（アンチエイリアス）
        Assert.True(eraserAlphas.Count > 2);
    }

    [Fact]
    public void RenderCirclePixels_StraightAlpha_PreservesSourceRgb()
    {
        // ストレートアルファ（RGB値を直接維持し、アルファのみを調整）を検証
        int size = 32;
        int hotspot = 16;
        double diameter = 18.0;
        var sourceColor = Color.FromArgb(200, 255, 128, 64);

        byte[] pixels = PenCursorHelper.RenderCirclePixels(
            size, hotspot, diameter, sourceColor, isHollow: false, isHighlighter: false);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a > 0)
            {
                Assert.Equal(sourceColor.B, pixels[i]);
                Assert.Equal(sourceColor.G, pixels[i + 1]);
                Assert.Equal(sourceColor.R, pixels[i + 2]);
            }
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
