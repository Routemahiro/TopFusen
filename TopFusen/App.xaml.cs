using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TopFusen.Services;

namespace TopFusen;

/// <summary>
/// TopFusen アプリケーション エントリポイント
/// 
/// - ShutdownMode = OnExplicitShutdown（トレイ常駐のため）
/// - 単一インスタンス制御（Mutex + NamedPipe）
/// - DI コンテナによるサービス管理
/// - Serilog によるログ出力
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private ServiceProvider? _serviceProvider;

    /// <summary>
    /// DI コンテナから取得したサービスプロバイダ
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. ログ基盤の初期化（最優先）
        LoggingService.Initialize();
        Log.Information("アプリケーション起動開始");

        // 2. データディレクトリの作成
        AppDataPaths.EnsureDirectories();

        // 3. 単一インスタンスチェック
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            Log.Information("二重起動を検知。プロセスを終了します");
            LoggingService.Shutdown();
            Shutdown(0);
            return;
        }

        // IPC コマンド受信ハンドラ
        _singleInstance.CommandReceived += OnIpcCommandReceived;

        // 4. DI コンテナの構築
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        Log.Information("DI コンテナ構築完了");

        // 5. SessionEnding フック（Windows ログオフ/シャットダウン時の保存）
        SessionEnding += OnSessionEnding;

        Log.Information("アプリケーション起動完了（Phase 0: 空のWPFアプリとして動作中）");

        // Phase 0 ではメインウィンドウを出さない（トレイ常駐は Phase 1 で実装）
        // Phase 1 以降でトレイアイコンを設置し、メインウィンドウなし運用に移行
    }

    /// <summary>
    /// DI サービスの登録
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<SingleInstanceService>();

        // TODO: Phase 1 以降で NoteManager, PersistenceService 等を追加
    }

    /// <summary>
    /// IPC コマンド受信時のハンドラ（二重起動側からの通知）
    /// </summary>
    private void OnIpcCommandReceived(string command)
    {
        Log.Information("IPC コマンド処理: {Command}", command);

        Dispatcher.Invoke(() =>
        {
            switch (command)
            {
                case "SHOW_SETTINGS":
                    // TODO: Phase 11 で設定画面を前面に出す
                    Log.Information("設定画面表示コマンド受信（未実装）");
                    break;
                default:
                    Log.Warning("不明な IPC コマンド: {Command}", command);
                    break;
            }
        });
    }

    /// <summary>
    /// Windows セッション終了時（ログオフ/シャットダウン）
    /// </summary>
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Log.Information("Windows セッション終了検知（理由: {Reason}）", e.ReasonSessionEnding);
        // TODO: Phase 5 で永続化のフラッシュ保存を行う
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("アプリケーション終了処理開始");

        // TODO: Phase 5 で永続化のフラッシュ保存を行う

        _singleInstance?.Dispose();
        _serviceProvider?.Dispose();

        LoggingService.Shutdown();

        base.OnExit(e);
    }
}
