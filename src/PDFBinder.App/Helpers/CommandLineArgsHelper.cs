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
/// <param name="PrimaryFile">現在のプロセスで開くべき先頭のPDFファイルパス（存在しない場合はnull）</param>
/// <param name="AdditionalFiles">別プロセスで開くべき後続のPDFファイルパス一覧</param>
public readonly record struct CommandLineArgsResult(
    string? PrimaryFile,
    IReadOnlyList<string> AdditionalFiles);

/// <summary>
/// 起動時コマンドライン引数の解析、PDFファイルパスの抽出、および外部プロセス起動を行うヘルパークラス
/// </summary>
public static class CommandLineArgsHelper
{
    /// <summary>
    /// 引数一覧からPDFファイルパスを抽出し、先頭ファイルと後続ファイルに分類します。
    /// </summary>
    /// <param name="args">コマンドライン引数のコレクション</param>
    /// <returns>解析結果（PrimaryFile, AdditionalFiles）</returns>
    public static CommandLineArgsResult Parse(IEnumerable<string>? args)
    {
        if (args == null)
        {
            return new CommandLineArgsResult(null, Array.Empty<string>());
        }

        var pdfFiles = new List<string>();
        foreach (var rawArg in args)
        {
            if (string.IsNullOrWhiteSpace(rawArg)) continue;

            // 前後の引用符や空白をトリム
            var arg = rawArg.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(arg)) continue;

            // オプション引数（- または / で始まるもの）は除外
            if (arg.StartsWith('-') || arg.StartsWith('/')) continue;

            // .pdf 拡張子を持つ引数のみを抽出
            if (!arg.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var fullPath = Path.GetFullPath(arg);
                pdfFiles.Add(fullPath);
            }
            catch (Exception)
            {
                // 不正な文字が含まれるパスはスキップ
            }
        }

        if (pdfFiles.Count == 0)
        {
            return new CommandLineArgsResult(null, Array.Empty<string>());
        }

        var primary = pdfFiles[0];
        var additional = pdfFiles.Skip(1).ToList();
        return new CommandLineArgsResult(primary, additional);
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
