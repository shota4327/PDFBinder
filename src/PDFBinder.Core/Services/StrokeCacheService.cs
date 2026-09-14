using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PDFBinder.Core.Services;

/// <summary>
/// 手書きストロークをWPF RenderTargetBitmapを活用して透過ビットマップに高速レンダリングするキャッシュサービス
/// </summary>
public class StrokeCacheService : IStrokeCacheService
{
    /// <inheritdoc/>
    public BitmapSource? RenderStrokeCache(
        StrokeCollection? strokes,
        double pageWidth,
        double pageHeight,
        int pixelWidth,
        int pixelHeight)
    {
        if (strokes == null || strokes.Count == 0 || pageWidth <= 0 || pageHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
        {
            return null;
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double scaleX = (double)pixelWidth / pageWidth;
            double scaleY = (double)pixelHeight / pageHeight;
            dc.PushTransform(new ScaleTransform(scaleX, scaleY));
            strokes.Draw(dc);
            dc.Pop();
        }

        var renderTarget = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }
}
