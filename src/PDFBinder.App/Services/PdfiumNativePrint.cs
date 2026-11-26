using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PDFBinder.App.Services;

/// <summary>
/// 同梱済み pdfium.dll を用いた Win32 GDI 直接印刷用の P/Invoke 宣言およびドキュメントキャッシュ管理
/// </summary>
internal static class PdfiumNativePrint
{
    public const int FPDF_ANNOT = 0x01;
    public const int FPDF_PRINTING = 0x800;
    public const int FPDF_PRINTMODE_EMF = 0;

    private static bool _resolverRegistered;

    static PdfiumNativePrint()
    {
        RegisterDllResolver();
    }

    /// <summary>
    /// runtimes 配下を含む出力ディレクトリから pdfium.dll を確実に解決するためのリゾルバを登録します。
    /// </summary>
    private static void RegisterDllResolver()
    {
        if (_resolverRegistered) return;
        _resolverRegistered = true;

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(PdfiumNativePrint).Assembly, ResolvePdfiumLibrary);
        }
        catch
        {
            // 既に別箇所で登録済みの場合は既定解決に委ねる
        }
    }

    /// <summary>
    /// pdfium.dll のロードパスを解決します。
    /// </summary>
    private static nint ResolvePdfiumLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "pdfium", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(libraryName, "pdfium.dll", StringComparison.OrdinalIgnoreCase))
        {
            return nint.Zero;
        }

        string archDir = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "pdfium.dll"),
            Path.Combine(baseDir, "runtimes", archDir, "native", "pdfium.dll")
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
            {
                return handle;
            }
        }

        return nint.Zero;
    }

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FPDF_LoadMemDocument(
        nint dataBuf,
        int size,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_CloseDocument(nint document);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint FPDF_LoadPage(nint document, int pageIndex);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_ClosePage(nint page);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_RenderPage(
        nint hdc,
        nint page,
        int startX,
        int startY,
        int sizeX,
        int sizeY,
        int rotate,
        int flags);

    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FPDF_SetPrintMode(int mode);
}

/// <summary>
/// 1つの印刷ジョブ内で開いた PDFium ドキュメントおよびピン留めメモリをキャッシュ・解放するヘルパー
/// </summary>
internal sealed class PdfiumPrintDocumentCache
{
    private readonly Dictionary<(string FilePath, string Password, bool UseCleanBytes), Entry> _entries = new();

    private sealed class Entry
    {
        public nint DocumentHandle { get; }
        public GCHandle PinnedBytes { get; }

        public Entry(nint documentHandle, GCHandle pinnedBytes)
        {
            DocumentHandle = documentHandle;
            PinnedBytes = pinnedBytes;
        }
    }

    /// <summary>
    /// 指定条件に対応する PDFium ドキュメントハンドルを取得または新規ロードします（PDFium ロック保持下で呼び出し）。
    /// </summary>
    public nint GetOrLoadDocument(string filePath, string? password, byte[]? cleanBytes)
    {
        bool useClean = cleanBytes != null;
        var key = (filePath, password ?? string.Empty, useClean);

        if (_entries.TryGetValue(key, out var existing))
        {
            return existing.DocumentHandle;
        }

        byte[] sourceBytes = cleanBytes ?? File.ReadAllBytes(filePath);
        if (sourceBytes.Length == 0)
        {
            return nint.Zero;
        }

        GCHandle pinned = GCHandle.Alloc(sourceBytes, GCHandleType.Pinned);
        try
        {
            nint doc = PdfiumNativePrint.FPDF_LoadMemDocument(
                pinned.AddrOfPinnedObject(),
                sourceBytes.Length,
                string.IsNullOrEmpty(password) ? null : password);

            if (doc == nint.Zero)
            {
                pinned.Free();
                return nint.Zero;
            }

            _entries[key] = new Entry(doc, pinned);
            return doc;
        }
        catch
        {
            if (pinned.IsAllocated)
            {
                pinned.Free();
            }
            return nint.Zero;
        }
    }

    /// <summary>
    /// キャッシュされたすべての PDFium ドキュメントを閉じてピン留めメモリを解放します（PDFium ロック保持下で呼び出し）。
    /// </summary>
    public void CloseAll()
    {
        foreach (var entry in _entries.Values)
        {
            try
            {
                if (entry.DocumentHandle != nint.Zero)
                {
                    PdfiumNativePrint.FPDF_CloseDocument(entry.DocumentHandle);
                }
            }
            finally
            {
                if (entry.PinnedBytes.IsAllocated)
                {
                    entry.PinnedBytes.Free();
                }
            }
        }

        _entries.Clear();
    }
}
