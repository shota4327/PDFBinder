using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PDFBinder.App.ViewModels;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;
using Xunit;

namespace PDFBinder.Tests;

/// <summary>
/// サムネイル生成処理とページ分割コマンドの並行実行・安全な中断待機を検証するテストクラス（Issue #139）
/// </summary>
public class SplitConcurrentCrashTests
{
    /// <summary>
    /// レンダリング処理に意図的な遅延を挟み、非同期実行中の状態をシミュレートするテスト用モックレンダラー
    /// </summary>
    private class SlowMockRenderer : IPdfRenderer
    {
        private readonly int _delayMs;
        public int RenderCallCount { get; private set; }
        public int CanceledCallCount { get; private set; }

        public SlowMockRenderer(int delayMs = 50)
        {
            _delayMs = delayMs;
        }

        public Task<BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
            => RenderPageAsync(filePath, pageIndex, targetWidth, targetHeight, rotation, cancellationToken, RenderPriority.Normal);

        public async Task<BitmapSource?> RenderPageAsync(
            string? filePath,
            int pageIndex,
            int targetWidth,
            int targetHeight,
            PageRotation rotation,
            CancellationToken cancellationToken,
            RenderPriority priority)
        {
            RenderCallCount++;
            try
            {
                await Task.Delay(_delayMs, cancellationToken);
                return CreateBlankPageBitmap(targetWidth, targetHeight, rotation);
            }
            catch (OperationCanceledException)
            {
                CanceledCallCount++;
                throw;
            }
        }

        public BitmapSource CreateBlankPageBitmap(int targetWidth, int targetHeight, PageRotation rotation)
        {
            return BitmapSource.Create(
                Math.Max(1, targetWidth),
                Math.Max(1, targetHeight),
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                new byte[Math.Max(1, targetWidth) * Math.Max(1, targetHeight) * 4],
                Math.Max(1, targetWidth) * 4);
        }

        public BitmapSource CompositeStrokes(
            BitmapSource baseBitmap,
            System.Windows.Ink.StrokeCollection strokes,
            double pageDisplayWidth,
            double pageDisplayHeight) => baseBitmap;

        public Task<List<PdfTextCharacter>> ExtractTextAsync(
            string? filePath,
            int pageIndex,
            double displayWidth,
            double displayHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new List<PdfTextCharacter>());

        public Task<PageInteractiveData> ExtractInteractiveDataAsync(
            string? filePath,
            int pageIndex,
            double displayWidth,
            double displayHeight,
            PageRotation rotation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(PageInteractiveData.Empty);
    }

    [Fact]
    public async Task SplitPagesHalfCommand_DuringThumbnailGeneration_CancelsThumbnailsAndSplitsSafely()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"PDFBinder_ConcurrentTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string pdfPath = Path.Combine(tempDir, "sample.pdf");

        try
        {
            // 4ページの横長（842x595）PDFを作成
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                for (int i = 0; i < 4; i++)
                {
                    var p = doc.AddPage();
                    p.Width = PdfSharp.Drawing.XUnit.FromPoint(842);
                    p.Height = PdfSharp.Drawing.XUnit.FromPoint(595);
                }
                doc.Save(pdfPath);
            }

            // Arrange
            var mockRenderer = new SlowMockRenderer(delayMs: 80);
            var pdfService = new PdfService();
            var undoRedoService = new UndoRedoService();
            var vm = new MainViewModel(pdfService, mockRenderer, undoRedoService);

            // グリッドビューに設定
            vm.IsDetailViewActive = false;

            for (int i = 0; i < 4; i++)
            {
                vm.Document.AddPage(new PdfPageModel
                {
                    SourceFilePath = pdfPath,
                    OriginalPageIndex = i,
                    Width = 842,
                    Height = 595,
                    Rotation = PageRotation.Rotate0,
                    IsThumbnailDirty = true
                });
            }
            Assert.Equal(4, vm.Document.PageCount);

            // Act 1: サムネイル生成をバックグラウンドで開始（非同期タスクが走る）
            var thumbTask = vm.EnsureThumbnailsGeneratedAsync();
            Assert.False(thumbTask.IsCompleted);

            // サムネイル生成が開始された直後（先頭ページの処理中）にページ分割を実行
            await Task.Delay(20);
            await vm.SplitPagesHalfCommand.ExecuteAsync(null);

            // Assert
            // 1. ページ分割が正常に完了し、ページ数が 4 -> 8 に倍増していること
            Assert.Equal(8, vm.Document.PageCount);
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal(421, vm.Document.Pages[i].Width);
                Assert.Equal(595, vm.Document.Pages[i].Height);
                Assert.Equal(i + 1, vm.Document.Pages[i].PageNumber);
            }

            // 2. 進行中タスクは完了しており、エラーなく終了していること
            Assert.False(vm.IsLoading);
            Assert.Contains("分割しました", vm.StatusMessage);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task CancelAndAwaitThumbnailsAsync_WhenNoThumbnailRunning_CompletesSafely()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act & Assert: サムネイル生成タスクが存在しない状態で呼び出しても例外が発生せず正常完了すること
        await vm.CancelAndAwaitThumbnailsAsync();
        Assert.Null(vm.CurrentThumbnailTask);
    }

    [Fact]
    public async Task SplitPagesHalfCommand_WithRealPdfium_DuringThumbnailGeneration_DoesNotCrashAndSucceeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"PDFBinder_RealPdfiumTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        string pdfPath = Path.Combine(tempDir, "sample_real.pdf");

        try
        {
            // 4ページの横長（842x595）PDFを作成
            using (var doc = new PdfSharp.Pdf.PdfDocument())
            {
                for (int i = 0; i < 4; i++)
                {
                    var p = doc.AddPage();
                    p.Width = PdfSharp.Drawing.XUnit.FromPoint(842);
                    p.Height = PdfSharp.Drawing.XUnit.FromPoint(595);
                    using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(p);
                    gfx.DrawRectangle(PdfSharp.Drawing.XBrushes.LightBlue, 10, 10, 800, 500);
                }
                doc.Save(pdfPath);
            }

            // Arrange: 実際の PdfiumRenderer を使用
            var realRenderer = new PdfiumRenderer();
            var pdfService = new PdfService();
            var undoRedoService = new UndoRedoService();
            var vm = new MainViewModel(pdfService, realRenderer, undoRedoService);

            // グリッドビューに設定
            vm.IsDetailViewActive = false;

            for (int i = 0; i < 4; i++)
            {
                vm.Document.AddPage(new PdfPageModel
                {
                    SourceFilePath = pdfPath,
                    OriginalPageIndex = i,
                    Width = 842,
                    Height = 595,
                    Rotation = PageRotation.Rotate0,
                    IsThumbnailDirty = true
                });
            }

            // Act: サムネイル生成をバックグラウンドで開始し、直ちに分割を実行
            _ = vm.EnsureThumbnailsGeneratedAsync();
            await vm.SplitPagesHalfCommand.ExecuteAsync(null);

            // Assert: クラッシュすることなく 8 ページに分割が完了すること
            Assert.Equal(8, vm.Document.PageCount);
            Assert.False(vm.IsLoading);
            Assert.Contains("分割しました", vm.StatusMessage);

            // 分割後の各ページにサムネイルが正しく生成されること
            await vm.EnsureThumbnailsGeneratedAsync();
            foreach (var page in vm.Document.Pages)
            {
                Assert.NotNull(page.Thumbnail);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
