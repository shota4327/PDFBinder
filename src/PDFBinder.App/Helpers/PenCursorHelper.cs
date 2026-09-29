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
    /// 直線モード用の定規アイコン付き円形プレビューカーソルを生成します。
    /// </summary>
    public static Cursor CreateStraightLineCursor(double diameter, Color color, bool isHighlighter)
    {
        const int badgeSize = 32;
        const double margin = 3.0;

        double radius = diameter / 2.0;
        double offset = (radius + margin) * 0.7071;

        int needed = (int)Math.Ceiling(Math.Max(radius, offset + badgeSize)) + 2;
        int hotspot = Math.Max(needed, 6);
        int size = hotspot * 2;

        byte[] bgraPixels = RenderCirclePixels(size, hotspot, diameter, color, isHollow: false, isHighlighter: isHighlighter);

        int badgeLeft = (int)Math.Round(hotspot + 0.5 + offset);
        int badgeTop = (int)Math.Round(hotspot + 0.5 - offset - badgeSize);

        DrawRulerBadge(bgraPixels, size, badgeLeft, badgeTop);

        byte[] curBytes = BuildCurBytes(size, size, hotspot, hotspot, bgraPixels);
        using var stream = new MemoryStream(curBytes);
        return new Cursor(stream);
    }

    /// <summary>
    /// カーソルピクセル配列（32-bit BGRA、ボトムアップ行順）上の指定座標に定規バッジを合成描画します（32x32px、4x4スーパーサンプリング）。
    /// </summary>
    internal static void DrawRulerBadge(byte[] pixels, int canvasSize, int startX, int startY)
    {
        const int badgeSize = 32;
        for (int y = 0; y < badgeSize; y++)
        {
            for (int x = 0; x < badgeSize; x++)
            {
                RenderBadgePixel(pixels, canvasSize, startX + x, startY + y, x, y);
            }
        }
    }

    /// <summary>
    /// 4x4スーパーサンプリング（16サンプル）により単一ピクセルのアンチエイリアス色を計算し、下地へブレンド描画します。
    /// </summary>
    private static void RenderBadgePixel(byte[] pixels, int canvasSize, int screenX, int screenY, int bx, int by)
    {
        if (screenX < 0 || screenX >= canvasSize || screenY < 0 || screenY >= canvasSize) return;

        double sumR = 0, sumG = 0, sumB = 0, sumA = 0;
        for (int sy = 0; sy < 4; sy++)
        {
            double v = by + (sy + 0.5) / 4.0;
            for (int sx = 0; sx < 4; sx++)
            {
                double u = bx + (sx + 0.5) / 4.0;
                SampleRulerColor(u, v, out byte sr, out byte sg, out byte sb, out byte sa);
                sumR += sr * (sa / 255.0);
                sumG += sg * (sa / 255.0);
                sumB += sb * (sa / 255.0);
                sumA += sa;
            }
        }

        if (sumA <= 0.0) return;

        double avgA = sumA / 16.0;
        byte finalA = (byte)Math.Clamp(Math.Round(avgA), 0, 255);
        byte finalR = (byte)Math.Clamp(Math.Round(sumR / (sumA / 255.0)), 0, 255);
        byte finalG = (byte)Math.Clamp(Math.Round(sumG / (sumA / 255.0)), 0, 255);
        byte finalB = (byte)Math.Clamp(Math.Round(sumB / (sumA / 255.0)), 0, 255);

        int dibRow = canvasSize - 1 - screenY;
        int pixelOffset = (dibRow * canvasSize + screenX) * 4;
        BlendBadgePixel(pixels, pixelOffset, finalR, finalG, finalB, finalA);
    }

    /// <summary>
    /// サブピクセル座標における定規の色（SDF：符号付き距離関数に基づく輪郭・白フチ・目盛り・本体）をサンプリングします。
    /// </summary>
    private static void SampleRulerColor(double u, double v, out byte r, out byte g, out byte b, out byte a)
    {
        double du = u - 15.5;
        double dv = v - 15.5;
        double len = (du - dv) * 0.70710678;
        double perp = (du + dv) * 0.70710678;

        const double lHalf = 13.5;
        const double wHalf = 4.0;
        const double cornerRadius = 1.5;

        double qx = Math.Max(Math.Abs(len) - (lHalf - cornerRadius), 0.0);
        double qy = Math.Max(Math.Abs(perp) - (wHalf - cornerRadius), 0.0);
        double dOut = Math.Sqrt(qx * qx + qy * qy) - cornerRadius;
        double dIn = Math.Min(Math.Max(Math.Abs(len) - lHalf, Math.Abs(perp) - wHalf), 0.0);
        double sdf = dOut > 0 ? dOut : dIn;

        if (sdf > 1.4)
        {
            r = g = b = a = 0;
            return;
        }

        if (sdf > 0.0)
        {
            r = g = b = 0;
            a = (byte)Math.Clamp(Math.Round(180.0 * (1.0 - (sdf / 1.4))), 0, 180);
            return;
        }

        if (sdf > -1.2 || IsOnTickMark(len, perp, wHalf))
        {
            r = g = b = a = 255; // 白フチ・目盛り
            return;
        }

        // 定規本体（スレートダーク: #1E293B）
        r = 30;
        g = 41;
        b = 59;
        a = 255;
    }

    /// <summary>
    /// 指定された座標が定規の目盛り位置上にあるかを判定します。
    /// </summary>
    private static bool IsOnTickMark(double len, double perp, double wHalf)
    {
        double depth = perp + wHalf;
        double tickIndex = Math.Round(len / 2.5);
        double nearestTickLen = tickIndex * 2.5;

        if (Math.Abs(nearestTickLen) > 10.5)
        {
            return false;
        }

        double distToTick = Math.Abs(len - nearestTickLen);
        bool isMajor = Math.Abs(tickIndex % 2.0) < 0.1;
        double maxDepth = isMajor ? 3.5 : 2.2;

        return distToTick <= 0.65 && depth >= 0.0 && depth <= maxDepth;
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
