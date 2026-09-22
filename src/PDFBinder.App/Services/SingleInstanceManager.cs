using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PDFBinder.App.Helpers;

namespace PDFBinder.App.Services;

/// <summary>
/// プロセス間通信で送受信する起動引数ペイロード
/// </summary>
public sealed class SingleInstancePayload
{
    /// <summary>開くべきPDFファイルのパス一覧</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>新しいウィンドウでの起動を強制するかどうか</summary>
    public bool ForceNewWindow { get; set; }
}

/// <summary>
/// 名前付きミューテックスおよび名前付きパイプを用いた単一インスタンス制御とプロセス間通信（IPC）を管理するクラス
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const int ConnectionTimeoutMs = 800;
    private const string AckResponse = "ACK";

    private readonly string _mutexName;
    private readonly string _pipeName;

    private Mutex? _mutex;
    private bool _hasMutexOwnership;
    private CancellationTokenSource? _serverCts;
    private Task? _serverLoopTask;
    private NamedPipeServerStream? _currentServerStream;
    private readonly object _serverStreamLock = new();
    private bool _isDisposed;

    /// <summary>
    /// <see cref="SingleInstanceManager"/> の新しいインスタンスを初期化します。
    /// </summary>
    public SingleInstanceManager()
    {
        var suffix = GetSafeUserSuffix();
        _mutexName = $"Local\\PDFBinder_SingleInstance_Mutex_{suffix}";
        _pipeName = $"PDFBinder_SingleInstance_Pipe_{suffix}";
    }

    /// <summary>
    /// 単一インスタンスのミューテックスの取得を試行します。
    /// </summary>
    /// <returns>プライマリインスタンスとしてミューテックスを取得できた場合は true、それ以外は false</returns>
    public bool TryAcquireOwnership()
    {
        if (_hasMutexOwnership) return true;

        try
        {
            _mutex = new Mutex(true, _mutexName, out bool createdNew);
            _hasMutexOwnership = createdNew;
            return createdNew;
        }
        catch (AbandonedMutexException)
        {
            // 前のプロセスが異常終了して放棄されたミューテックスを取得
            _hasMutexOwnership = true;
            return true;
        }
        catch (Exception)
        {
            _hasMutexOwnership = false;
            return false;
        }
    }

    /// <summary>
    /// 既存の起動中プライマリインスタンスへコマンドライン引数を名前付きパイプ経由で送信します。
    /// </summary>
    /// <param name="args">コマンドライン引数解析結果</param>
    /// <returns>既存インスタンスへの送信が成功した場合は true、接続不能またはタイムアウト時は false</returns>
    public async Task<bool> TrySendToExistingInstanceAsync(CommandLineArgsResult args)
    {
        var payload = new SingleInstancePayload
        {
            Files = args.Files.ToList(),
            ForceNewWindow = args.ForceNewWindow
        };

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(ConnectionTimeoutMs);

            await client.ConnectAsync(cts.Token);

            var json = JsonSerializer.Serialize(payload);
            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, leaveOpen: true);

            await writer.WriteLineAsync(json.AsMemory(), cts.Token);
            var response = await reader.ReadLineAsync(cts.Token);

            if (response == AckResponse)
            {
                WindowActivationHelper.GrantForegroundPermission();
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 名前付きパイプサーバーを開始し、外部プロセスからのリクエストを受信待機します。
    /// </summary>
    /// <param name="onPayloadReceived">ペイロード受信時の非同期コールバック</param>
    public void StartServer(Func<SingleInstancePayload, Task> onPayloadReceived)
    {
        if (_serverCts != null) return;

        _serverCts = new CancellationTokenSource();
        _serverLoopTask = Task.Run(() => RunServerLoopAsync(onPayloadReceived, _serverCts.Token));
    }

    /// <summary>
    /// 名前付きパイプサーバーの接続受信ループを実行します。
    /// </summary>
    private async Task RunServerLoopAsync(Func<SingleInstancePayload, Task> onPayloadReceived, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                lock (_serverStreamLock)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        pipeServer.Dispose();
                        break;
                    }
                    _currentServerStream = pipeServer;
                }

                using var reg = cancellationToken.Register(() =>
                {
                    try { pipeServer.Dispose(); } catch { }
                });

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                await ProcessClientSessionAsync(pipeServer, onPayloadReceived, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // 一時的な接続エラー等は無視して次回の受信待機を継続
            }
            finally
            {
                lock (_serverStreamLock)
                {
                    if (_currentServerStream == pipeServer)
                    {
                        _currentServerStream = null;
                    }
                }
                pipeServer?.Dispose();
            }
        }
    }

    /// <summary>
    /// クライアント接続からのメッセージを読み取り、処理・応答します。
    /// </summary>
    private static async Task ProcessClientSessionAsync(
        NamedPipeServerStream pipeServer,
        Func<SingleInstancePayload, Task> onPayloadReceived,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipeServer, leaveOpen: true);
        using var writer = new StreamWriter(pipeServer, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(line))
        {
            var payload = JsonSerializer.Deserialize<SingleInstancePayload>(line);
            await writer.WriteLineAsync(AckResponse.AsMemory(), cancellationToken);

            if (payload != null)
            {
                await onPayloadReceived(payload);
            }
        }
    }

    /// <summary>
    /// カレントユーザー名から安全な半角英数字サフィックスを生成します。
    /// </summary>
    private static string GetSafeUserSuffix()
    {
        var rawUser = Environment.UserName;
        var cleanChars = rawUser.Where(char.IsLetterOrDigit).ToArray();
        return cleanChars.Length > 0 ? new string(cleanChars) : "DefaultUser";
    }

    /// <summary>
    /// ミューテックスおよびサーバーリソースを解放します。
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _serverCts?.Cancel();
            lock (_serverStreamLock)
            {
                _currentServerStream?.Dispose();
                _currentServerStream = null;
            }
            _serverCts?.Dispose();
        }
        catch { }

        if (_hasMutexOwnership && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
        }

        _mutex?.Dispose();
    }
}
