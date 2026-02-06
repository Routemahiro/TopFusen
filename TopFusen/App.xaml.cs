using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
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
/// - タスクトレイ常駐 + 付箋管理（Phase 1）
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private ServiceProvider? _serviceProvider;
    private TaskbarIcon? _trayIcon;
    private NoteManager? _noteManager;

    /// <summary>編集モードメニュー項目（トグル表示更新用）</summary>
    private MenuItem? _editModeMenuItem;

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

        // 6. NoteManager 初期化
        _noteManager = _serviceProvider.GetRequiredService<NoteManager>();

        // 7. タスクトレイアイコン初期化
        InitializeTrayIcon();

        Log.Information("アプリケーション起動完了（Phase 2: トレイ常駐 + モード切替）");
    }

    /// <summary>
    /// DI サービスの登録
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<SingleInstanceService>();
        services.AddSingleton<NoteManager>();
    }

    /// <summary>
    /// タスクトレイアイコンの初期化
    /// XAML リソースから取得 → ContextMenu 設定 → ForceCreate() で shell に登録
    /// </summary>
    private void InitializeTrayIcon()
    {
        // App.xaml で定義した TaskbarIcon リソースを取得
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.ContextMenu = CreateTrayContextMenu();

        // ForceCreate() で shell notification icon を確実に作成
        _trayIcon.ForceCreate();

        Log.Information("トレイアイコンを初期化しました（ForceCreate 完了）");
    }

    /// <summary>
    /// トレイ右クリックメニューの構築（FR-TRAY）
    /// </summary>
    private ContextMenu CreateTrayContextMenu()
    {
        var menu = new ContextMenu();

        // --- 編集モード ON/OFF（FR-TRAY-1）---
        _editModeMenuItem = new MenuItem { Header = "✏️ 編集モード: OFF" };
        _editModeMenuItem.Click += (_, _) =>
        {
            if (_noteManager == null) return;

            var newMode = !_noteManager.IsEditMode;
            _noteManager.SetEditMode(newMode);
            _editModeMenuItem.Header = newMode
                ? "✏️ 編集モード: ON ✓"
                : "✏️ 編集モード: OFF";
        };
        menu.Items.Add(_editModeMenuItem);

        // --- 新規付箋作成（FR-TRAY-2）---
        var newNoteItem = new MenuItem { Header = "📝 新規付箋作成" };
        newNoteItem.Click += (_, _) =>
        {
            _noteManager?.CreateNote();
        };
        menu.Items.Add(newNoteItem);

        menu.Items.Add(new Separator());

        // --- 一時的に非表示（FR-TRAY-3）--- stub
        var hideItem = new MenuItem { Header = "👁 一時的に非表示" };
        hideItem.Click += (_, _) =>
        {
            // TODO: Phase 10 で実装
            Log.Information("一時非表示（未実装）");
        };
        menu.Items.Add(hideItem);

        // --- 設定を開く（FR-TRAY-4）--- stub
        var settingsItem = new MenuItem { Header = "⚙ 設定..." };
        settingsItem.Click += (_, _) =>
        {
            // TODO: Phase 11 で実装
            Log.Information("設定画面（未実装）");
        };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // --- 終了（FR-TRAY-5）---
        var exitItem = new MenuItem { Header = "✖ 終了" };
        exitItem.Click += (_, _) =>
        {
            Log.Information("終了メニューが選択されました");
            Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
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

        // 全付箋ウィンドウを閉じる
        _noteManager?.CloseAllWindows();

        // トレイアイコンの破棄
        _trayIcon?.Dispose();
        _trayIcon = null;

        _singleInstance?.Dispose();
        _serviceProvider?.Dispose();

        LoggingService.Shutdown();

        base.OnExit(e);
    }
}
