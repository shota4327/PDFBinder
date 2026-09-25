using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PDFBinder.App.Helpers;

/// <summary>
/// コマンドライン引数の解析結果を保持するレコード
/// </summary>
/// <param name="Files">開くべきPDFファイルパス一覧</param>
/// <param name="ForceNewWindow">新しいウィンドウでの起動を強制するかどうか</param>
public readonly record struct CommandLineArgsResult(
    IReadOnlyList<string> Files,
    bool ForceNewWindow)
{
    /// <summary>後方互換用: 先頭のPDFファイルパス（存在しない場合はnull）</summary>
    public string? PrimaryFile => Files.Count > 0 ? Files[0] : null;

    /// <summary>後方互換用: 先頭を除く後続のPDFファイルパス一覧</summary>
    public IReadOnlyList<string> AdditionalFiles => Files.Count > 1 ? Files.Skip(1).ToList() : Array.Empty<string>();
}

/// <summary>
/// 起動時コマンドライン引数の解析、PDFファイルパスの抽出、および外部プロセス起動を行うヘルパークラス
/// </summary>
public static class CommandLineArgsHelper
{
    /// <summary>
    /// 引数一覧からPDFファイルパスを抽出し、新規ウィンドウ指定オプションの有無を解析します。
    /// </summary>
    /// <param name="args">コマンドライン引数のコレクション</param>
    /// <returns>解析結果（Files, ForceNewWindow）</returns>
    public static CommandLineArgsResult Parse(IEnumerable<string>? args)
    {
        if (args == null)
        {
            return new CommandLineArgsResult(Array.Empty<string>(), false);
        }

        var pdfFiles = new List<string>();
        bool forceNewWindow = false;

        foreach (var rawArg in args)
        {
            if (string.IsNullOrWhiteSpace(rawArg)) continue;

            // 前後の引用符や空白をトリム
            var arg = rawArg.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(arg)) continue;

            // 新規ウィンドウ起動オプション（--new-window または -n）の判定
            if (string.Equals(arg, "--new-window", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-n", StringComparison.OrdinalIgnoreCase))
            {
                forceNewWindow = true;
                continue;
            }

            // サポート対象拡張子（.pdf, .jpg, .jpeg, .png）を持つ引数のみを抽出
            if (!IsSupportedFile(arg)) continue;

            try
            {
                var fullPath = Path.GetFullPath(arg);
                if (!pdfFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    pdfFiles.Add(fullPath);
                }
            }
            catch (Exception)
            {
                // 不正な文字が含まれるパスはスキップ
            }
        }

        return new CommandLineArgsResult(pdfFiles, forceNewWindow);
    }

    private static bool IsSupportedFile(string path)
    {
        string ext = Path.GetExtension(path);
        return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 指定されたPDFファイルパスを開くための追加プロセスの起動情報を生成します。
    /// </summary>
    /// <param name="filePath">開くPDFファイルのフルパス</param>
    /// <returns>ProcessStartInfo オブジェクト</returns>
    public static ProcessStartInfo CreateProcessStartInfo(string filePath)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
        {
            processPath = Environment.GetCommandLineArgs()[0];
        }

        var fileName = Path.GetFileName(processPath);
        if (fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(entryAssembly) && entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessStartInfo
                {
                    FileName = processPath,
                    Arguments = $"exec \"{entryAssembly}\" \"{filePath}\"",
                    UseShellExecute = false
                };
            }
        }

        return new ProcessStartInfo
        {
            FileName = processPath,
            Arguments = $"\"{filePath}\"",
            UseShellExecute = false
        };
    }

    /// <summary>
    /// 指定されたPDFファイルを別プロセスの PDF Binder で開きます。
    /// </summary>
    /// <param name="filePath">開くPDFファイルのフルパス</param>
    public static void LaunchAdditionalProcess(string filePath)
    {
        try
        {
            var startInfo = CreateProcessStartInfo(filePath);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"追加プロセスの起動に失敗しました: {ex.Message}");
        }
    }
}
