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

        // 6. NoteManager 初期化 + オーナーウィンドウ生成（DJ-7）
        _noteManager = _serviceProvider.GetRequiredService<NoteManager>();
        _noteManager.InitializeOwnerWindow();

        // 7. 仮想デスクトップサービス初期化（Phase 3.5 スパイク / DJ-4: UIスレッドで初期化）
        _vdService = _serviceProvider.GetRequiredService<VirtualDesktopService>();
        _vdService.Initialize();

        // 8. タスクトレイアイコン初期化
        InitializeTrayIcon();

        Log.Information("アプリケーション起動完了（Phase 3.5: トレイ常駐 + モード切替 + VDスパイク）");
    }

    /// <summary>
    /// DI サービスの登録
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SingleInstanceService>();
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

        // --- 設定を開く（FR-TRAY-4）--- stub
        var settingsItem = new MenuItem { Header = "⚙ 設定..." };
        settingsItem.Click += (_, _) =>
        {
            Log.Information("設定画面（未実装）");
        };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // --- Phase 3.5: 仮想デスクトップ スパイク検証メニュー ---
        var vdInfoItem = new MenuItem { Header = "🔬 VD: 情報取得テスト" };
        vdInfoItem.Click += OnVdSpikeInfoTest;
        menu.Items.Add(vdInfoItem);

        var vdMoveItem = new MenuItem { Header = "🔬 VD: 付箋移動テスト" };
        vdMoveItem.Click += OnVdSpikeMoveTest;
        menu.Items.Add(vdMoveItem);

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
    //  Phase 3.5: 仮想デスクトップ スパイク検証
    // ==========================================

    /// <summary>
    /// VD スパイク: COM初期化 + 現在デスクトップID + Registry一覧 を一括テスト
    /// 結果をメッセージボックスで表示
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
    /// VD スパイク: 移動テスト（診断強化版 v2）
    /// 1. まず普通の Window で API が動くか検証
    /// 2. NoteWindow で各種ワークアラウンドを試行
    /// </summary>
    private async void OnVdSpikeMoveTest(object sender, RoutedEventArgs e)
    {
        await Task.Delay(300);

        if (_vdService == null || !_vdService.IsAvailable)
        {
            MessageBox.Show("COM が利用不可です。", "VD検証", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentId = _vdService.GetCurrentDesktopId();
        if (currentId == null)
        {
            MessageBox.Show("現在デスクトップ ID が取得できません。", "VD検証", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var desktops = _vdService.GetDesktopListFromRegistry();
        var target = desktops.FirstOrDefault(d => d.Id != currentId.Value);
        if (target == default)
        {
            MessageBox.Show("移動先が見つかりません。\nWin+Tab で2つ以上のデスクトップを作成してください。",
                "VD検証", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== VD 移動テスト v2（診断モード） ===\n");
        sb.AppendLine($"現在VD: {currentId}");
        sb.AppendLine($"移動先: {target.Name} ({target.Id})\n");

        // ==========================================
        //  テスト 0: 普通の Window（スタイルなし）で移動テスト
        // ==========================================
        sb.AppendLine("[テスト 0] 普通の Window で MoveWindowToDesktop...");
        var testWin = new Window
        {
            Title = "VD Test",
            Width = 200, Height = 100,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = true,
            Topmost = false,
        };
        testWin.Show();
        await Task.Delay(200);

        var testHwnd = new WindowInteropHelper(testWin).Handle;
        var testDesktopBefore = _vdService.GetWindowDesktopId(testHwnd);
        var testResult = _vdService.MoveWindowToDesktop(testHwnd, target.Id);
        await Task.Delay(200);
        var testOnCurrent = _vdService.IsWindowOnCurrentDesktop(testHwnd);
        var testDesktopAfter = _vdService.GetWindowDesktopId(testHwnd);
        testWin.Close();

        sb.AppendLine($"  DesktopId 前: {testDesktopBefore}");
        sb.AppendLine($"  MoveResult: {testResult}");
        sb.AppendLine($"  OnCurrentDesktop: {testOnCurrent}");
        sb.AppendLine($"  DesktopId 後: {testDesktopAfter}");
        sb.AppendLine(testOnCurrent == false
            ? "  → ✅ 普通の Window は移動成功！API は正常\n"
            : "  → ❌ 普通の Window でも失敗（API 自体に問題あり）\n");

        // ==========================================
        //  テスト 1〜: NoteWindow で移動テスト
        // ==========================================
        if (_noteManager == null || _noteManager.Count == 0)
        {
            sb.AppendLine("[NoteWindow テスト] 付箋なし — スキップ");
            sb.AppendLine("先に「新規付箋作成」で作成してから再実行してください");
        }
        else
        {
            var noteWindow = _noteManager.Windows[0];
            var hwnd = new WindowInteropHelper(noteWindow).Handle;
            var exStyle = Interop.NativeMethods.GetWindowLong(hwnd, Interop.NativeMethods.GWL_EXSTYLE);

            sb.AppendLine($"[NoteWindow] ExStyle=0x{exStyle:X8}");
            sb.AppendLine($"  TOOLWINDOW={((exStyle & 0x80) != 0)}, TOPMOST={((exStyle & 8) != 0)}, LAYERED={((exStyle & 0x80000) != 0)}, TRANSPARENT={((exStyle & 0x20) != 0)}, NOACTIVATE={((exStyle & 0x8000000) != 0)}\n");

            // テスト 1A: そのまま Move + GetWindowDesktopId
            sb.AppendLine("[テスト 1A] そのまま Move...");
            var desktopBefore = _vdService.GetWindowDesktopId(hwnd);
            var res1a = _vdService.MoveWindowToDesktop(hwnd, target.Id);
            await Task.Delay(300);
            var on1a = _vdService.IsWindowOnCurrentDesktop(hwnd);
            var desktop1a = _vdService.GetWindowDesktopId(hwnd);
            sb.AppendLine($"  DesktopId 前: {desktopBefore}");
            sb.AppendLine($"  Move={res1a}, OnCurrent={on1a}, DesktopId後={desktop1a}");
            sb.AppendLine(desktopBefore != desktop1a ? "  → ✅ DesktopId が変わった！" : "  → DesktopId 変化なし");

            if (on1a != false)
            {
                // テスト 1B: WS_EX_TRANSPARENT/NOACTIVATE を外して Move
                sb.AppendLine("\n[テスト 1B] TRANSPARENT + NOACTIVATE を外して Move...");
                var cleanStyle = exStyle & ~Interop.NativeMethods.WS_EX_TRANSPARENT
                                         & ~Interop.NativeMethods.WS_EX_NOACTIVATE;
                Interop.NativeMethods.SetWindowLong(hwnd, Interop.NativeMethods.GWL_EXSTYLE, cleanStyle);
                await Task.Delay(100);
                var res1b = _vdService.MoveWindowToDesktop(hwnd, target.Id);
                await Task.Delay(300);
                var on1b = _vdService.IsWindowOnCurrentDesktop(hwnd);
                var desktop1b = _vdService.GetWindowDesktopId(hwnd);
                sb.AppendLine($"  Move={res1b}, OnCurrent={on1b}, DesktopId後={desktop1b}");
                // スタイル復元
                Interop.NativeMethods.SetWindowLong(hwnd, Interop.NativeMethods.GWL_EXSTYLE, exStyle);

                if (on1b != false)
                {
                    // テスト 1C: Hide → Move → Show
                    sb.AppendLine("\n[テスト 1C] Hide → Move → (Show しない)...");
                    noteWindow.Hide();
                    await Task.Delay(100);
                    var hwnd2 = new WindowInteropHelper(noteWindow).Handle;
                    var res1c = _vdService.MoveWindowToDesktop(hwnd2, target.Id);
                    await Task.Delay(300);
                    var desktop1c = _vdService.GetWindowDesktopId(hwnd2);
                    sb.AppendLine($"  Move={res1c}, DesktopId後={desktop1c}");
                    // 再表示
                    noteWindow.Show();
                    noteWindow.Topmost = true;

                    if (desktop1c.HasValue && desktop1c.Value == target.Id)
                    {
                        sb.AppendLine("  → ✅ Hide→Move で DesktopId 変更成功！Show 後に戻った可能性あり");
                    }
                    else
                    {
                        sb.AppendLine("  → ❌ Hide→Move でも失敗");
                    }
                }
                else
                {
                    sb.AppendLine("  → ✅ TRANSPARENT/NOACTIVATE 解除で成功！");
                }
            }
            else
            {
                sb.AppendLine("  → ✅ そのまま Move で成功！");
            }
        }

        MessageBox.Show(sb.ToString(), "VD検証: 移動テスト v2",
            MessageBoxButton.OK, MessageBoxImage.Information);

        Log.Information("[VD スパイク] 移動テスト v2:\n{Result}", sb.ToString());
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
    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Log.Information("Windows セッション終了検知（理由: {Reason}）", e.ReasonSessionEnding);
        // TODO: Phase 5 で永続化のフラッシュ保存を行う
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("アプリケーション終了処理開始");

        // 全付箋ウィンドウを閉じる
        _noteManager?.CloseAllWindows();

        // 仮想デスクトップサービスの COM 解放
        _vdService?.Dispose();

        // トレイアイコンの破棄
        _trayIcon?.Dispose();
        _trayIcon = null;

        _singleInstance?.Dispose();
        _serviceProvider?.Dispose();

        LoggingService.Shutdown();

        base.OnExit(e);
    }
}
