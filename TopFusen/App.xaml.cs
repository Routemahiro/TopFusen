using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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
/// - タスクトレイ常駐 + 付箋管理
/// - Phase 3.5: 仮想デスクトップ技術スパイク
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private ServiceProvider? _serviceProvider;
    private TaskbarIcon? _trayIcon;
    private NoteManager? _noteManager;
    private VirtualDesktopService? _vdService;
    private PersistenceService? _persistence;

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

        // 6. PersistenceService 取得（Phase 5）
        _persistence = _serviceProvider.GetRequiredService<PersistenceService>();

        // 7. NoteManager 初期化 + オーナーウィンドウ生成（DJ-7）
        _noteManager = _serviceProvider.GetRequiredService<NoteManager>();
        _noteManager.InitializeOwnerWindow();

        // 8. 仮想デスクトップサービス初期化（DJ-4: UIスレッドで / LoadAll より前に初期化必須）
        _vdService = _serviceProvider.GetRequiredService<VirtualDesktopService>();
        _vdService.Initialize();
        _vdService.InitializeTracker(_noteManager.OwnerHandle);

        // 9. 保存データから付箋を復元（起動直後は編集OFF — FR-BOOT-2）
        //    VD サービス初期化後に呼ぶこと（RestoreNote 内で VD Cloak + P8-6 フォールバックが必要）
        _noteManager.LoadAll();

        // 10. Phase 5: 破損からの復旧通知
        if (_persistence.CorruptionRecovered)
        {
            Log.Warning("設定ファイル破損を検知し、バックアップから復旧しました");
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    MessageBox.Show(
                        _persistence.RecoveryMessage ?? "設定ファイルが破損していたため、バックアップから復旧しました。",
                        "TopFusen — データ復旧通知",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }));
        }

        // 11. Phase 8: デスクトップ監視開始（LoadAll 後に開始）
        _vdService.DesktopChanged += OnDesktopChanged;
        _vdService.StartDesktopMonitoring();

        // 11. タスクトレイアイコン初期化
        InitializeTrayIcon();

        Log.Information("アプリケーション起動完了（Phase 8: トレイ常駐 + モード切替 + 永続化 + VD自前管理）");
    }

    /// <summary>
    /// DI サービスの登録
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SingleInstanceService>();
        services.AddSingleton<PersistenceService>();
        services.AddSingleton<NoteManager>();
        services.AddSingleton<VirtualDesktopService>();
    }

    /// <summary>
    /// タスクトレイアイコンの初期化
    /// XAML リソースから取得 → ContextMenu 設定 → ForceCreate() で shell に登録
    /// </summary>
    private void InitializeTrayIcon()
    {
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.ContextMenu = CreateTrayContextMenu();
        _trayIcon.ForceCreate();

        Log.Information("トレイアイコンを初期化しました（ForceCreate 完了）");
    }

    /// <summary>
    /// トレイ右クリックメニューの構築（FR-TRAY + Phase 3.5 スパイクメニュー）
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
            Log.Information("一時非表示（未実装）");
        };
        menu.Items.Add(hideItem);

        // --- Z順管理（Phase 9）---
        var zOrderItem = new MenuItem { Header = "📊 Z順管理..." };
        zOrderItem.Click += async (_, _) =>
        {
            // トレイメニューが閉じるのを待つ
            await Task.Delay(200);
            if (_noteManager == null) return;
            var zOrderWindow = new Views.ZOrderWindow(_noteManager);
            zOrderWindow.ShowDialog();
        };
        menu.Items.Add(zOrderItem);

        // --- 設定を開く（FR-TRAY-4）--- stub
        var settingsItem = new MenuItem { Header = "⚙ 設定..." };
        settingsItem.Click += (_, _) =>
        {
            Log.Information("設定画面（未実装）");
        };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // --- Phase 8: VD デバッグメニュー ---
        var vdInfoItem = new MenuItem { Header = "🔬 VD: 情報取得" };
        vdInfoItem.Click += OnVdSpikeInfoTest;
        menu.Items.Add(vdInfoItem);

        var vdCloakItem = new MenuItem { Header = "🔬 VD: Cloak/Uncloak 確認" };
        vdCloakItem.Click += OnVdCloakTest;
        menu.Items.Add(vdCloakItem);

        var vdStatusItem = new MenuItem { Header = "🔬 VD: 全付箋状態" };
        vdStatusItem.Click += OnVdStatusTest;
        menu.Items.Add(vdStatusItem);

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

    // ==========================================
    //  Phase 8: VD デバッグ機能
    // ==========================================

    /// <summary>
    /// VD 情報取得: COM状態 + 現在デスクトップID + Registry一覧 を表示
    /// ★ async + Delay で ContextMenu が完全に閉じてから MessageBox を表示する
    ///   （H.NotifyIcon のトレイメニューから直接 MessageBox を出すと一瞬で消える問題の回避）
    /// </summary>
    private async void OnVdSpikeInfoTest(object sender, RoutedEventArgs e)
    {
        if (_vdService == null) return;

        // トレイメニューが完全に閉じるのを待つ
        await Task.Delay(300);

        var sb = new StringBuilder();
        sb.AppendLine("=== 仮想デスクトップ スパイク検証 ===\n");

        // Test 1: COM 状態
        sb.AppendLine($"[P3.5-1] COM 利用可能: {_vdService.IsAvailable}");

        // Test 2: 現在デスクトップ ID（短命ウィンドウ方式）
        var currentId = _vdService.GetCurrentDesktopId();
        sb.AppendLine($"[P3.5-2] 現在デスクトップ ID: {currentId?.ToString() ?? "取得失敗"}");

        // Test 3: Registry デスクトップ一覧
        var desktops = _vdService.GetDesktopListFromRegistry();
        sb.AppendLine($"\n[P3.5-4] Registry デスクトップ数: {desktops.Count}");

        if (desktops.Count == 0)
        {
            sb.AppendLine("  ※ 一覧が空（デスクトップ1つのみ、または値なし）");
        }
        else
        {
            foreach (var (id, name) in desktops)
            {
                var isCurrent = (currentId.HasValue && id == currentId.Value) ? " ← 現在" : "";
                sb.AppendLine($"  - {name}: {id}{isCurrent}");
            }
        }

        // 付箋の IsWindowOnCurrentDesktop テスト
        if (_noteManager != null && _noteManager.Count > 0 && _vdService.IsAvailable)
        {
            sb.AppendLine("\n[IsWindowOnCurrentDesktop テスト]");
            foreach (var window in _noteManager.Windows)
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var onCurrent = _vdService.IsWindowOnCurrentDesktop(hwnd);
                sb.AppendLine($"  - {window.Model.NoteId:N}: OnCurrent={onCurrent}");
            }
        }

        MessageBox.Show(sb.ToString(), "VD スパイク: 情報取得テスト",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// VD Cloak 確認 — 最初の付箋を Cloak → OK 後に Uncloak して動作を確認
    /// </summary>
    private async void OnVdCloakTest(object sender, RoutedEventArgs e)
    {
        await Task.Delay(300);

        if (_noteManager == null || _noteManager.Count == 0)
        {
            MessageBox.Show("付箋がありません。先に「新規付箋作成」で作成してください。",
                "VD Cloak テスト", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var noteWindow = _noteManager.Windows[0];
        var hwnd = new WindowInteropHelper(noteWindow).Handle;

        var sb = new StringBuilder();
        sb.AppendLine("=== DWMWA_CLOAK テスト ===\n");
        sb.AppendLine($"対象: {noteWindow.Model.NoteId}");
        sb.AppendLine($"HWND: 0x{hwnd.ToInt64():X}");

        // Cloak
        sb.AppendLine("\n[1] CloakWindow 実行...");
        VirtualDesktopService.CloakWindow(hwnd);
        sb.AppendLine("  → 付箋が消えたはず（3秒後に Uncloak）");

        MessageBox.Show(sb + "\n\nOK を押すと 3 秒後に Uncloak します", "VD Cloak テスト（Cloak 中）",
            MessageBoxButton.OK, MessageBoxImage.Information);

        // Uncloak
        await Task.Delay(1000);
        VirtualDesktopService.UncloakWindow(hwnd);

        MessageBox.Show("Uncloak 完了！\n付箋が再表示され、Topmost が維持されているか確認してください。",
            "VD Cloak テスト（Uncloak 完了）", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 全付箋の VD 状態を一覧表示する（DesktopId / WS_EX_TRANSPARENT / 所属判定）
    /// </summary>
    private async void OnVdStatusTest(object sender, RoutedEventArgs e)
    {
        await Task.Delay(300);

        var sb = new StringBuilder();
        sb.AppendLine("=== VD 全付箋状態 ===\n");

        // 現在の VD
        var currentId = _vdService?.GetCurrentDesktopIdFast();
        sb.AppendLine($"現在の VD: {currentId?.ToString() ?? "取得失敗"}");

        // Registry から VD 一覧
        var desktops = _vdService?.GetDesktopListFromRegistry() ?? new();
        sb.AppendLine($"VD 数: {desktops.Count}");
        foreach (var (id, name) in desktops)
        {
            var isCurrent = (currentId.HasValue && id == currentId.Value) ? " ← 現在" : "";
            sb.AppendLine($"  - {name}: {id}{isCurrent}");
        }

        sb.AppendLine();

        // 各付箋の VD 状態
        if (_noteManager != null && _noteManager.Count > 0)
        {
            sb.AppendLine($"付箋数: {_noteManager.Count}");
            foreach (var window in _noteManager.Windows)
            {
                var model = window.Model;
                var hwnd = new WindowInteropHelper(window).Handle;
                var exStyle = Interop.NativeMethods.GetWindowLong(hwnd, Interop.NativeMethods.GWL_EXSTYLE);
                var hasTransparent = (exStyle & Interop.NativeMethods.WS_EX_TRANSPARENT) != 0;
                var belongsHere = model.DesktopId == Guid.Empty || (currentId.HasValue && model.DesktopId == currentId.Value);

                sb.AppendLine($"\n  [{model.NoteId:N}]");
                sb.AppendLine($"    DesktopId: {model.DesktopId}");
                sb.AppendLine($"    WS_EX_TRANSPARENT: {hasTransparent}");
                sb.AppendLine($"    現在VDに所属: {belongsHere}");
                sb.AppendLine($"    Preview: {model.FirstLinePreview}");
            }
        }
        else
        {
            sb.AppendLine("付箋なし");
        }

        MessageBox.Show(sb.ToString(), "VD 全付箋状態",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==========================================
    //  Phase 8: VD 切替ハンドラ
    // ==========================================

    /// <summary>
    /// VD 切替検知時のハンドラ — NoteManager に表示制御を委譲
    /// </summary>
    private void OnDesktopChanged(Guid newDesktopId)
    {
        _noteManager?.HandleDesktopSwitch(newDesktopId);
    }

    // ==========================================
    //  IPC / Session / Exit
    // ==========================================

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
    /// <summary>
    /// Windows セッション終了時（ログオフ/シャットダウン）— Phase 5: 強制保存
    /// </summary>
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Log.Information("Windows セッション終了検知（理由: {Reason}）— 保存フラッシュ実行", e.ReasonSessionEnding);
        _persistence?.FlushSave();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("アプリケーション終了処理開始");

        // Phase 5: 終了前に保存をフラッシュ（ウィンドウがまだ開いている間に）
        _persistence?.FlushSave();

        // 全付箋ウィンドウを閉じる
        _noteManager?.CloseAllWindows();

        // 仮想デスクトップ: 監視停止 + Tracker 閉じ + COM 解放
        _vdService?.Dispose();

        // トレイアイコンの破棄
        _trayIcon?.Dispose();
        _trayIcon = null;

        // PersistenceService の Dispose（デバウンスタイマー停止）
        _persistence?.Dispose();

        _singleInstance?.Dispose();
        _serviceProvider?.Dispose();

        LoggingService.Shutdown();

        base.OnExit(e);
    }
}
