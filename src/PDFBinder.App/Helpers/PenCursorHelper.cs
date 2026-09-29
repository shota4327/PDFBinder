using System.Collections.Concurrent;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using PDFBinder.App.Controls;

namespace PDFBinder.App.Helpers;

/// <summary>
/// ズーム倍率およびストローク太さに連動したペン・蛍光ペン・消しゴム用の円形カーソルを生成・キャッシュするヘルパークラス
/// </summary>
public static class PenCursorHelper
{
    /// <summary>カーソルの最小表示直径（ピクセル）</summary>
    public const double MinCursorSize = 3.0;

    /// <summary>カーソルの最大表示直径（ピクセル）</summary>
    public const double MaxCursorSize = 128.0;

    /// <summary>生成済みカーソルのキャッシュ</summary>
    private static readonly ConcurrentDictionary<string, Cursor> CursorCache = new();

    /// <summary>
    /// 指定されたツール・描画色・太さ・ズーム倍率・直線モード設定に応じたカーソルを取得します。
    /// 円形カーソル対象外ツールの場合は null を返します。
    /// </summary>
    public static Cursor? GetCursor(
        EditorToolMode toolMode, Color color, double strokeThickness, double zoom, bool isStraightLine = false)
    {
        bool isStraight = isStraightLine || toolMode == EditorToolMode.StraightLine;
        if (!IsCircleCursorTool(toolMode, isStraight))
        {
            return null;
        }

        double scaledDiameter = strokeThickness * (zoom > 0 ? zoom : 1.0);
        double clampedDiameter = Math.Clamp(scaledDiameter, MinCursorSize, MaxCursorSize);
        int roundedDiameterHalfPx = (int)Math.Round(clampedDiameter * 2.0);

        string cacheKey = $"{toolMode}_{color.A}_{color.R}_{color.G}_{color.B}_{roundedDiameterHalfPx}_{(isStraight ? "SL" : "NORM")}";
        return CursorCache.GetOrAdd(cacheKey, _ => CreateCursor(toolMode, color, clampedDiameter, isStraight));
    }

    /// <summary>
    /// 対象ツールが円形プレビューカーソルを適用するツールであるかを判定します。
    /// </summary>
    public static bool IsCircleCursorTool(EditorToolMode toolMode, bool isStraightLine = false) =>
        toolMode is EditorToolMode.Pen or EditorToolMode.Highlighter or EditorToolMode.EraserPoint or EditorToolMode.StraightLine
        || (isStraightLine && toolMode is EditorToolMode.Pen or EditorToolMode.Highlighter);

    /// <summary>キャッシュされたWPF標準ストローク消しゴム形状カーソル</summary>
    private static Cursor? _strokeEraserCursor;

    /// <summary>
    /// キャッシュをすべてクリアします（テスト用）。
    /// </summary>
    public static void ClearCache() => CursorCache.Clear();

    /// <summary>
    /// WPF標準のストローク消しゴム形状（消しゴムアイコン）カーソルを取得します。
    /// </summary>
    public static Cursor GetStrokeEraserCursor()
    {
        if (_strokeEraserCursor != null)
        {
            return _strokeEraserCursor;
        }

        try
        {
            var type = typeof(System.Windows.Controls.InkCanvas).Assembly.GetType("MS.Internal.Ink.PenCursorManager");
            var method = type?.GetMethod("GetStrokeEraserCursor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (method?.Invoke(null, null) is Cursor cursor)
            {
                _strokeEraserCursor = cursor;
                return _strokeEraserCursor;
            }
        }
        catch
        {
            // リフレクション取得失敗時はフォールバック
        }

        return Cursors.Arrow;
    }

    /// <summary>
    /// ツール種別および直線モード設定に応じた円形カーソルを生成します。
    /// </summary>
    private static Cursor CreateCursor(EditorToolMode toolMode, Color color, double diameter, bool isStraightLine)
    {
        bool isHollow = toolMode == EditorToolMode.EraserPoint;
        bool isHighlighter = toolMode == EditorToolMode.Highlighter;
        return CreateCircleCursor(diameter, color, isHollow, isHighlighter, isStraightLine);
    }

    /// <summary>
    /// 直径・色・塗りつぶし設定から32-bit ARGB DIBカーソルストリームを構築し、WPF Cursorを生成します。
    /// </summary>
    public static Cursor CreateCircleCursor(
        double diameter, Color color, bool isHollow, bool isHighlighter, bool isStraightLine = false)
    {
        if (isStraightLine)
        {
            return CreateStraightLineCursor(diameter, color, isHighlighter);
        }

        int size = Math.Max((int)Math.Ceiling(diameter) + 4, 6);
        int hotspot = size / 2;

        byte[] bgraPixels = RenderCirclePixels(size, hotspot, diameter, color, isHollow, isHighlighter);
        byte[] curBytes = BuildCurBytes(size, size, hotspot, hotspot, bgraPixels);

        using var stream = new MemoryStream(curBytes);
        return new Cursor(stream);
    }

    /// <summary>
    /// 直線モード用の十字線付き円形プレビューカーソルを生成します。
    /// </summary>
    public static Cursor CreateStraightLineCursor(double diameter, Color color, bool isHighlighter)
    {
        const double gap = 3.5;
        const double minLineLen = 7.0;
        const double lineThickness = 2.0;

        double lineLen = Math.Max(minLineLen, diameter * (2.0 / 3.0));
        double radius = diameter / 2.0;
        int needed = (int)Math.Ceiling(radius + gap + lineLen) + 2;
        int hotspot = Math.Clamp(needed, 6, 128);
        int size = hotspot * 2;

        byte[] bgraPixels = RenderCirclePixels(size, hotspot, diameter, color, isHollow: false, isHighlighter: isHighlighter);

        DrawCrosshairLines(bgraPixels, size, hotspot, radius, gap, lineLen, lineThickness, color);

        byte[] curBytes = BuildCurBytes(size, size, hotspot, hotspot, bgraPixels);
        using var stream = new MemoryStream(curBytes);
        return new Cursor(stream);
    }

    /// <summary>
    /// 直線モード用の十字線（クロスヘア）を円形プレビューの上下左右に描画します。
    /// </summary>
    internal static void DrawCrosshairLines(
        byte[] pixels, int canvasSize, int hotspot, double radius, double gap, double lineLen, double thickness = 2.0, Color? lineColor = null)
    {
        Color col = lineColor ?? Colors.Black;
        double cx = hotspot + 0.5;
        double cy = hotspot + 0.5;
        double halfThick = thickness / 2.0;

        // 上
        DrawLineRect(pixels, canvasSize, cx - halfThick, cx + halfThick, cy - radius - gap - lineLen, cy - radius - gap, col);
        // 下
        DrawLineRect(pixels, canvasSize, cx - halfThick, cx + halfThick, cy + radius + gap, cy + radius + gap + lineLen, col);
        // 左
        DrawLineRect(pixels, canvasSize, cx - radius - gap - lineLen, cx - radius - gap, cy - halfThick, cy + halfThick, col);
        // 右
        DrawLineRect(pixels, canvasSize, cx + radius + gap, cx + radius + gap + lineLen, cy - halfThick, cy + halfThick, col);
    }

    /// <summary>
    /// 指定された矩形範囲を指定色の直線として描画（サブピクセルカバレッジ計算によるアンチエイリアス）します。
    /// </summary>
    private static void DrawLineRect(
        byte[] pixels, int canvasSize, double minX, double maxX, double minY, double maxY, Color color)
    {
        int xStart = Math.Max(0, (int)Math.Floor(minX));
        int xEnd = Math.Min(canvasSize - 1, (int)Math.Ceiling(maxX));
        int yStart = Math.Max(0, (int)Math.Floor(minY));
        int yEnd = Math.Min(canvasSize - 1, (int)Math.Ceiling(maxY));

        for (int y = yStart; y <= yEnd; y++)
        {
            double py0 = y;
            double py1 = y + 1.0;
            double yOverlap = Math.Max(0.0, Math.Min(py1, maxY) - Math.Max(py0, minY));
            if (yOverlap <= 0.0) continue;

            int dibRow = canvasSize - 1 - y;
            int rowOffset = dibRow * canvasSize * 4;

            for (int x = xStart; x <= xEnd; x++)
            {
                double px0 = x;
                double px1 = x + 1.0;
                double xOverlap = Math.Max(0.0, Math.Min(px1, maxX) - Math.Max(px0, minX));
                if (xOverlap <= 0.0) continue;

                double coverage = xOverlap * yOverlap;
                if (coverage > 0.0)
                {
                    int pixelOffset = rowOffset + x * 4;
                    ApplyLinePixel(pixels, pixelOffset, coverage, color);
                }
            }
        }
    }

    /// <summary>
    /// 指定色ラインのピクセルを下地へアルファ合成します。
    /// </summary>
    private static void ApplyLinePixel(byte[] pixels, int pixelOffset, double coverage, Color color)
    {
        byte alpha = (byte)Math.Clamp(Math.Round(255.0 * coverage), 0, 255);
        if (alpha == 0) return;

        double sA = alpha / 255.0;
        double dA = pixels[pixelOffset + 3] / 255.0;
        double outA = sA + dA * (1.0 - sA);

        if (outA <= 0.0) return;

        double outB = (color.B * sA + pixels[pixelOffset + 0] * dA * (1.0 - sA)) / outA;
        double outG = (color.G * sA + pixels[pixelOffset + 1] * dA * (1.0 - sA)) / outA;
        double outR = (color.R * sA + pixels[pixelOffset + 2] * dA * (1.0 - sA)) / outA;

        pixels[pixelOffset + 0] = (byte)Math.Clamp(Math.Round(outB), 0, 255);
        pixels[pixelOffset + 1] = (byte)Math.Clamp(Math.Round(outG), 0, 255);
        pixels[pixelOffset + 2] = (byte)Math.Clamp(Math.Round(outR), 0, 255);
        pixels[pixelOffset + 3] = (byte)Math.Clamp(Math.Round(outA * 255.0), 0, 255);
    }

    /// <summary>
    /// アルファブレンド（Over演算）により下地ピクセルへ合成します。
    /// </summary>
    private static void BlendBadgePixel(byte[] pixels, int pixelOffset, byte srcR, byte srcG, byte srcB, byte srcA)
    {
        double sA = srcA / 255.0;
        double dA = pixels[pixelOffset + 3] / 255.0;
        double outA = sA + dA * (1.0 - sA);

        if (outA <= 0.0) return;

        double outB = (srcB * sA + pixels[pixelOffset + 0] * dA * (1.0 - sA)) / outA;
        double outG = (srcG * sA + pixels[pixelOffset + 1] * dA * (1.0 - sA)) / outA;
        double outR = (srcR * sA + pixels[pixelOffset + 2] * dA * (1.0 - sA)) / outA;

        pixels[pixelOffset + 0] = (byte)Math.Clamp(Math.Round(outB), 0, 255);
        pixels[pixelOffset + 1] = (byte)Math.Clamp(Math.Round(outG), 0, 255);
        pixels[pixelOffset + 2] = (byte)Math.Clamp(Math.Round(outR), 0, 255);
        pixels[pixelOffset + 3] = (byte)Math.Clamp(Math.Round(outA * 255.0), 0, 255);
    }

    /// <summary>
    /// 円形カーソルのピクセル配列（32-bit BGRA、ボトムアップ行順）を描画・生成します。
    /// </summary>
    internal static byte[] RenderCirclePixels(
        int size, int hotspot, double diameter, Color color, bool isHollow, bool isHighlighter)
    {
        byte[] pixels = new byte[size * size * 4];
        double cx = hotspot + 0.5;
        double cy = hotspot + 0.5;
        double radius = diameter / 2.0;
        double strokeWidth = 1.5;
        double halfStroke = Math.Min(strokeWidth / 2.0, radius * 0.4);

        for (int y = 0; y < size; y++)
        {
            // DIBはボトムアップ行順（先頭が最下行）
            int dibRow = size - 1 - y;
            int rowOffset = dibRow * size * 4;

            for (int x = 0; x < size; x++)
            {
                int pixelOffset = rowOffset + x * 4;
                DrawCirclePixel(pixels, pixelOffset, x, y, cx, cy, radius, halfStroke, color, isHollow, isHighlighter);
            }
        }

        return pixels;
    }

    /// <summary>
    /// 単一ピクセルの色をアンチエイリアス処理（8x8スーパーサンプリング）で設定します。
    /// </summary>
    private static void DrawCirclePixel(
        byte[] pixels, int pixelOffset, int x, int y, double cx, double cy,
        double radius, double halfStroke, Color color, bool isHollow, bool isHighlighter)
    {
        double distCenter = Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2));
        int hitCount = CalculateSubpixelHits(x, y, cx, cy, radius, halfStroke, distCenter, isHollow);

        if (hitCount <= 0)
        {
            return;
        }

        double coverage = hitCount / 64.0;
        byte baseAlpha = isHollow ? (byte)220 : (isHighlighter ? (byte)140 : color.A);
        byte alpha = (byte)Math.Round(baseAlpha * coverage);

        if (alpha > 0)
        {
            pixels[pixelOffset] = isHollow ? (byte)0 : color.B;     // B
            pixels[pixelOffset + 1] = isHollow ? (byte)0 : color.G; // G
            pixels[pixelOffset + 2] = isHollow ? (byte)0 : color.R; // R
            pixels[pixelOffset + 3] = alpha;                         // A
        }
    }

    /// <summary>
    /// 距離判定と8x8スーパーサンプリング（64サンプル）によりサブピクセル内包含数を算出します。
    /// </summary>
    private static int CalculateSubpixelHits(
        int x, int y, double cx, double cy, double radius, double halfStroke,
        double distCenter, bool isHollow)
    {
        if (isHollow)
        {
            // 部分消しゴム（中心ドットなしの滑らかな中空輪郭線）
            double ringDist = Math.Abs(distCenter - radius);
            if (ringDist <= halfStroke - 0.75)
            {
                return 64;
            }
            if (ringDist >= halfStroke + 0.75)
            {
                return 0;
            }
        }
        else
        {
            // ペン / 蛍光ペン（滑らかな塗りつぶし円）
            if (distCenter <= radius - 0.75)
            {
                return 64;
            }
            if (distCenter >= radius + 0.75)
            {
                return 0;
            }
        }

        // 境界ピクセルのみ8x8サンプリングを実行
        return SampleBoundaryHits(x, y, cx, cy, radius, halfStroke, isHollow);
    }

    /// <summary>
    /// サブピクセル8x8グリッド上のサンプルヒット数を集計します。
    /// </summary>
    private static int SampleBoundaryHits(
        int x, int y, double cx, double cy, double radius, double halfStroke, bool isHollow)
    {
        int hitCount = 0;
        for (int sy = 0; sy < 8; sy++)
        {
            double py = y + (sy + 0.5) / 8.0;
            for (int sx = 0; sx < 8; sx++)
            {
                double px = x + (sx + 0.5) / 8.0;
                double dist = Math.Sqrt(Math.Pow(px - cx, 2) + Math.Pow(py - cy, 2));

                if (isHollow)
                {
                    if (Math.Abs(dist - radius) <= halfStroke)
                    {
                        hitCount++;
                    }
                }
                else
                {
                    if (dist <= radius)
                    {
                        hitCount++;
                    }
                }
            }
        }

        return hitCount;
    }

    /// <summary>
    /// Windows標準の .cur バイナリ形式バイト列を構築します。
    /// </summary>
    private static byte[] BuildCurBytes(int width, int height, int hotspotX, int hotspotY, byte[] bgraPixels)
    {
        int maskRowBytes = ((width + 31) / 32) * 4;
        int maskSize = maskRowBytes * height;
        int colorSize = width * height * 4;
        int dibSize = 40 + colorSize + maskSize;
        int totalSize = 6 + 16 + dibSize;

        byte[] cur = new byte[totalSize];
        using var ms = new MemoryStream(cur);
        using var writer = new BinaryWriter(ms);

        // ICONDIR (6 bytes)
        writer.Write((ushort)0); // Reserved
        writer.Write((ushort)2); // Type = 2 (Cursor)
        writer.Write((ushort)1); // Image Count = 1

        // ICONDIRENTRY (16 bytes)
        writer.Write((byte)(width >= 256 ? 0 : width));
        writer.Write((byte)(height >= 256 ? 0 : height));
        writer.Write((byte)0);   // Color count
        writer.Write((byte)0);   // Reserved
        writer.Write((ushort)hotspotX);
        writer.Write((ushort)hotspotY);
        writer.Write((uint)dibSize);
        writer.Write((uint)22);  // Image data offset (6 + 16 = 22)

        // BITMAPINFOHEADER (40 bytes)
        writer.Write((uint)40);         // Header size
        writer.Write((int)width);        // Width
        writer.Write((int)(height * 2)); // Combined height (Color + Mask)
        writer.Write((ushort)1);         // Planes
        writer.Write((ushort)32);        // BitCount (32-bit ARGB)
        writer.Write((uint)0);           // Compression (BI_RGB)
        writer.Write((uint)(colorSize + maskSize));
        writer.Write((int)0);            // XPelsPerMeter
        writer.Write((int)0);            // YPelsPerMeter
        writer.Write((uint)0);           // ClrUsed
        writer.Write((uint)0);           // ClrImportant

        // XOR Color Map (BGRA pixels)
        writer.Write(bgraPixels);

        // AND Mask (all 0 for 32-bit alpha transparency)
        writer.Write(new byte[maskSize]);

        return cur;
    }
}
