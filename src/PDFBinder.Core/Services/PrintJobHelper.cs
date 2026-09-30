using System.IO;

namespace PDFBinder.Core.Services;

/// <summary>
/// 印刷ジョブ名に関する計算・フォーマットを行うヘルパークラス
/// </summary>
public static class PrintJobHelper
{
    private const string AppSuffix = "PDFBinder";
    private const string DefaultTitle = "名称未設定";

    /// <summary>
    /// ドキュメント名（またはファイル名・パス）から印刷ジョブ名を生成します。
    /// 拡張子を除去し、「{ファイル名} - PDFBinder」形式で返します。
    /// 未保存または空文字の場合は「名称未設定 - PDFBinder」を返します。
    /// </summary>
    /// <param name="fileNameOrPath">ファイル名またはドキュメントタイトル</param>
    /// <returns>印刷ジョブ名</returns>
    public static string GenerateJobName(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return $"{DefaultTitle} - {AppSuffix}";
        }

        string fileName;
        try
        {
            fileName = Path.GetFileName(fileNameOrPath);
        }
        catch
        {
            fileName = fileNameOrPath;
        }

        string nameWithoutExtension;
        try
        {
            nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        }
        catch
        {
            nameWithoutExtension = fileName;
        }

        if (string.IsNullOrWhiteSpace(nameWithoutExtension))
        {
            nameWithoutExtension = DefaultTitle;
        }

        return $"{nameWithoutExtension} - {AppSuffix}";
    }
}
