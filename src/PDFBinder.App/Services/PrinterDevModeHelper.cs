using System.Printing;
using System.Printing.Interop;
using System.Runtime.InteropServices;
using PDFBinder.Core.Models;
using PDFBinder.Core.Services;

namespace PDFBinder.App.Services;

/// <summary>
/// プリンターの DEVMODE 構造体操作およびプロパティシート呼び出しを行うヘルパークラス
/// </summary>
internal static class PrinterDevModeHelper
{
    private const int DM_IN_BUFFER = 8;
    private const int DM_IN_PROMPT = 4;
    private const int DM_OUT_BUFFER = 2;
    private const int IDOK = 1;

    // DEVMODE dmFields ビットフラグ
    private const uint DM_ORIENTATION = 0x00000001;
    private const uint DM_PAPERSIZE = 0x00000002;
    private const uint DM_PAPERLENGTH = 0x00000004;
    private const uint DM_PAPERWIDTH = 0x00000008;
    private const uint DM_COPIES = 0x00000100;
    private const uint DM_DUPLEX = 0x00001000;

    // DEVMODE 値定義
    private const short DMORIENT_PORTRAIT = 1;
    private const short DMORIENT_LANDSCAPE = 2;
    private const short DMPAPER_A3 = 8;
    private const short DMPAPER_A4 = 9;
    private const short DMDUP_SIMPLEX = 1;
    private const short DMDUP_VERTICAL = 2;
    private const short DMDUP_HORIZONTAL = 3;

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string pPrinterName, out nint phPrinter, nint pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(nint hPrinter);

    [DllImport("winspool.drv", EntryPoint = "DocumentPropertiesW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int DocumentProperties(
        nint hWnd,
        nint hPrinter,
        string pDeviceName,
        nint pDevModeOutput,
        nint pDevModeInput,
        int fMode);

    /// <summary>
    /// プリンターの「印刷設定」ダイアログをモーダル表示し、ユーザーが確定した設定値を取得します。
    /// </summary>
    public static PrinterSettingsDialogResult? ShowDocumentPropertiesDialog(
        string printerName,
        nint ownerHwnd,
        byte[]? currentDevMode)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return null;

        if (!OpenPrinter(printerName, out nint hPrinter, nint.Zero) || hPrinter == nint.Zero)
        {
            return null;
        }

        try
        {
            return ExecuteDocumentProperties(printerName, ownerHwnd, hPrinter, currentDevMode);
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    /// <summary>
    /// DocumentProperties Win32 API を呼び出してダイアログを表示し、結果を解析します。
    /// </summary>
    private static PrinterSettingsDialogResult? ExecuteDocumentProperties(
        string printerName,
        nint ownerHwnd,
        nint hPrinter,
        byte[]? currentDevMode)
    {
        int requiredSize = DocumentProperties(ownerHwnd, hPrinter, printerName, nint.Zero, nint.Zero, 0);
        if (requiredSize <= 0) return null;

        nint pOut = Marshal.AllocHGlobal(requiredSize);
        nint pIn = nint.Zero;

        try
        {
            int mode = DM_IN_PROMPT | DM_OUT_BUFFER;
            if (currentDevMode != null && currentDevMode.Length > 0)
            {
                pIn = Marshal.AllocHGlobal(currentDevMode.Length);
                Marshal.Copy(currentDevMode, 0, pIn, currentDevMode.Length);
                mode |= DM_IN_BUFFER;
            }

            int result = DocumentProperties(ownerHwnd, hPrinter, printerName, pOut, pIn, mode);
            if (result != IDOK) return null;

            byte[] devModeBytes = new byte[requiredSize];
            Marshal.Copy(pOut, devModeBytes, 0, requiredSize);

            return ParseDevMode(devModeBytes);
        }
        finally
        {
            if (pOut != nint.Zero) Marshal.FreeHGlobal(pOut);
            if (pIn != nint.Zero) Marshal.FreeHGlobal(pIn);
        }
    }

    /// <summary>
    /// DEVMODE バイト配列から用紙、向き、部数、両面印刷設定を読み取ります。
    /// </summary>
    internal static PrinterSettingsDialogResult ParseDevMode(byte[] devModeBytes)
    {
        // DEVMODEW 構造体の基本ヘッダー（少なくとも 96 バイト以上）の存在を確認
        if (devModeBytes.Length < 96)
        {
            return new PrinterSettingsDialogResult(devModeBytes, null, null, null, null);
        }

        uint dmFields = BitConverter.ToUInt32(devModeBytes, 72);
        short dmOrientation = BitConverter.ToInt16(devModeBytes, 76);
        short dmPaperSize = BitConverter.ToInt16(devModeBytes, 78);
        short dmCopies = BitConverter.ToInt16(devModeBytes, 86);
        short dmDuplex = BitConverter.ToInt16(devModeBytes, 94);

        PrintOrientation? orientation = (dmFields & DM_ORIENTATION) != 0
            ? (dmOrientation == DMORIENT_LANDSCAPE ? PrintOrientation.Landscape : PrintOrientation.Portrait)
            : null;

        PrintPaperSize? paperSize = (dmFields & DM_PAPERSIZE) != 0
            ? (dmPaperSize == DMPAPER_A3 ? PrintPaperSize.A3 : dmPaperSize == DMPAPER_A4 ? PrintPaperSize.A4 : null)
            : null;

        int? copies = (dmFields & DM_COPIES) != 0 && dmCopies > 0
            ? Math.Clamp((int)dmCopies, 1, 99)
            : null;

        PrintDuplexMode? duplex = (dmFields & DM_DUPLEX) != 0
            ? dmDuplex switch
            {
                DMDUP_VERTICAL => PrintDuplexMode.TwoSidedLongEdge,
                DMDUP_HORIZONTAL => PrintDuplexMode.TwoSidedShortEdge,
                _ => PrintDuplexMode.OneSided
            }
            : null;

        return new PrinterSettingsDialogResult(devModeBytes, paperSize, orientation, copies, duplex);
    }

    /// <summary>
    /// DEVMODE バイナリデータから WPF の PrintTicket を生成します。
    /// </summary>
    public static PrintTicket? CreatePrintTicketFromDevMode(
        string printerName,
        byte[] devModeData,
        PrintQueue printQueue)
    {
        if (devModeData == null || devModeData.Length == 0 || string.IsNullOrWhiteSpace(printerName))
        {
            return null;
        }

        try
        {
            using var converter = new PrintTicketConverter(printerName, printQueue.ClientPrintSchemaVersion);
            return converter.ConvertDevModeToPrintTicket(devModeData);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// プリンターの既定または保持中の DEVMODE に印刷設定をパッチし、ドライバー検証済みの DEVMODE を生成します。
    /// </summary>
    public static byte[]? BuildEffectiveDevMode(
        string printerName,
        PrintSettings settings,
        out bool driverHandlesCopies)
    {
        driverHandlesCopies = settings.Copies <= 1;
        if (string.IsNullOrWhiteSpace(printerName)) return null;

        if (!OpenPrinter(printerName, out nint hPrinter, nint.Zero) || hPrinter == nint.Zero)
        {
            return null;
        }

        try
        {
            return MergeAndValidateDevMode(printerName, hPrinter, settings, out driverHandlesCopies);
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    /// <summary>
    /// DocumentProperties を用いて DEVMODE の初期化・パッチ適用・ドライバー統合を実行します。
    /// </summary>
    private static byte[]? MergeAndValidateDevMode(
        string printerName,
        nint hPrinter,
        PrintSettings settings,
        out bool driverHandlesCopies)
    {
        driverHandlesCopies = settings.Copies <= 1;
        int requiredSize = DocumentProperties(nint.Zero, hPrinter, printerName, nint.Zero, nint.Zero, 0);
        if (requiredSize < 96) return null;

        byte[]? baseDevMode = ReadInitialDevModeBytes(printerName, hPrinter, requiredSize, settings.DriverDevMode);
        if (baseDevMode == null) return null;

        PatchDevModeBytes(baseDevMode, settings);

        byte[]? validated = RunDocumentPropertiesMerge(printerName, hPrinter, requiredSize, baseDevMode);
        if (validated == null) return baseDevMode;

        if (settings.Copies > 1 && validated.Length >= 96)
        {
            uint outFields = BitConverter.ToUInt32(validated, 72);
            short outCopies = BitConverter.ToInt16(validated, 86);
            driverHandlesCopies = (outFields & DM_COPIES) != 0 && outCopies >= settings.Copies;
        }

        return validated;
    }

    /// <summary>
    /// 既存の DriverDevMode またはプリンター既定の DEVMODE バイト配列を取得します。
    /// </summary>
    private static byte[]? ReadInitialDevModeBytes(
        string printerName,
        nint hPrinter,
        int requiredSize,
        byte[]? currentDevMode)
    {
        if (currentDevMode != null && currentDevMode.Length > 0)
        {
            byte[]? normalized = RunDocumentPropertiesMerge(printerName, hPrinter, requiredSize, currentDevMode);
            if (normalized != null) return normalized;
        }

        return RunDocumentPropertiesMerge(printerName, hPrinter, requiredSize, null);
    }

    /// <summary>
    /// DocumentProperties(DM_OUT_BUFFER / DM_IN_BUFFER) を安全に呼び出して DEVMODE バイト配列を返します。
    /// </summary>
    private static byte[]? RunDocumentPropertiesMerge(
        string printerName,
        nint hPrinter,
        int requiredSize,
        byte[]? inputBytes)
    {
        nint pOut = Marshal.AllocHGlobal(requiredSize);
        nint pIn = nint.Zero;

        try
        {
            int mode = DM_OUT_BUFFER;
            if (inputBytes != null && inputBytes.Length > 0)
            {
                pIn = Marshal.AllocHGlobal(inputBytes.Length);
                Marshal.Copy(inputBytes, 0, pIn, inputBytes.Length);
                mode |= DM_IN_BUFFER;
            }

            int result = DocumentProperties(nint.Zero, hPrinter, printerName, pOut, pIn, mode);
            if (result != IDOK) return null;

            byte[] output = new byte[requiredSize];
            Marshal.Copy(pOut, output, 0, requiredSize);
            return output;
        }
        finally
        {
            if (pOut != nint.Zero) Marshal.FreeHGlobal(pOut);
            if (pIn != nint.Zero) Marshal.FreeHGlobal(pIn);
        }
    }

    /// <summary>
    /// DEVMODE バイト配列へ用紙サイズ・向き・部数・両面設定を上書きします。
    /// </summary>
    internal static void PatchDevModeBytes(byte[] devModeBytes, PrintSettings settings)
    {
        if (devModeBytes == null || devModeBytes.Length < 96) return;

        uint dmFields = BitConverter.ToUInt32(devModeBytes, 72);
        dmFields &= ~(DM_PAPERLENGTH | DM_PAPERWIDTH);
        dmFields |= DM_ORIENTATION | DM_PAPERSIZE | DM_COPIES | DM_DUPLEX;

        short orientation = settings.Orientation == PrintOrientation.Landscape
            ? DMORIENT_LANDSCAPE
            : DMORIENT_PORTRAIT;
        short paperSize = settings.PaperSize == PrintPaperSize.A3
            ? DMPAPER_A3
            : DMPAPER_A4;
        short copies = (short)Math.Clamp(settings.Copies, 1, 99);
        short duplex = settings.DuplexMode switch
        {
            PrintDuplexMode.TwoSidedLongEdge => DMDUP_VERTICAL,
            PrintDuplexMode.TwoSidedShortEdge => DMDUP_HORIZONTAL,
            _ => DMDUP_SIMPLEX
        };

        BitConverter.GetBytes(dmFields).CopyTo(devModeBytes, 72);
        BitConverter.GetBytes(orientation).CopyTo(devModeBytes, 76);
        BitConverter.GetBytes(paperSize).CopyTo(devModeBytes, 78);
        BitConverter.GetBytes(copies).CopyTo(devModeBytes, 86);
        BitConverter.GetBytes(duplex).CopyTo(devModeBytes, 94);
    }
}
