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
}
