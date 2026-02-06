using System.IO;
using System.IO.Pipes;
using Serilog;

namespace TopFusen.Services;

/// <summary>
/// 単一インスタンス制御（Mutex + NamedPipe IPC）
/// 
/// - 名前付き Mutex でプロセス重複検知
/// - 2重起動時: 既存プロセスに NamedPipe で「設定を開く」コマンドを送り、新プロセスは終了
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "TopFusen_SingleInstance_Mutex";
    private const string PipeName = "TopFusen_SingleInstance_Pipe";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;
    private Task? _pipeListenerTask;

    /// <summary>
    /// 既存プロセスからコマンドを受信した時に発火するイベント
    /// </summary>
    public event Action<string>? CommandReceived;

    /// <summary>
    /// 単一インスタンスの取得を試みる
    /// </summary>
    /// <returns>true: このプロセスが唯一のインスタンス / false: 既に別プロセスが存在</returns>
    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (createdNew)
        {
            Log.Information("単一インスタンス取得成功（1番目のプロセス）");
            StartPipeListener();
            return true;
        }

        Log.Information("別プロセスが既に起動中。コマンドを送信して終了します");
        SendCommandToExistingInstance("SHOW_SETTINGS");
        return false;
    }

    /// <summary>
    /// NamedPipe のリスナーを開始する（既存プロセス側）
    /// </summary>
    private void StartPipeListener()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        _pipeListenerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var command = await reader.ReadLineAsync(token);

                    if (!string.IsNullOrEmpty(command))
                    {
                        Log.Information("IPC コマンド受信: {Command}", command);
                        CommandReceived?.Invoke(command);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "NamedPipe リスナーでエラー発生");
                }
            }
        }, token);
    }

    /// <summary>
    /// 既存プロセスにコマンドを送信する（2番目のプロセス側）
    /// </summary>
    private static void SendCommandToExistingInstance(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000); // 3秒タイムアウト

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);

            Log.Information("既存プロセスへコマンド送信完了: {Command}", command);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "既存プロセスへのコマンド送信に失敗");
        }
    }

    public void Dispose()
    {
        _pipeCts?.Cancel();

        try
        {
            _pipeListenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // タスクキャンセル時は無視
        }

        _pipeCts?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
