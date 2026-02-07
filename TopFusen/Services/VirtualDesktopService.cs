using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;
using TopFusen.Interop;

namespace TopFusen.Services;

/// <summary>
/// 仮想デスクトップ操作サービス
///
/// Phase 3.5: 技術スパイク（API 成立検証）
/// Phase 8.0: DJ-10 スパイク（Tracker Window + DWMWA_CLOAK + ポーリング）
/// Phase 8: 本格実装に昇格予定
///
/// DJ-4: COM 呼び出しは UI スレッド（STA）から行う
/// DJ-10: VD 自前管理（WS_EX_TRANSPARENT と OS VD 追跡は共存不可）
/// </summary>
public class VirtualDesktopService : IDisposable
{
    private IVirtualDesktopManager? _vdm;

    /// <summary>COM 初期化が成功し、利用可能かどうか</summary>
    public bool IsAvailable { get; private set; }

    // Registry パス定数
    private const string VD_REG_PATH
        = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const string VD_DESKTOPS_PATH
        = VD_REG_PATH + @"\Desktops";

    // --- Phase 8.0: VD Tracker + 監視 ---

    /// <summary>VD Tracker Window（WS_EX_TRANSPARENT なし、VD 追跡用の常駐 HWND）</summary>
    private Window? _trackerWindow;
    private IntPtr _trackerHwnd;

    /// <summary>ポーリングタイマー（VD 切替検知用）</summary>
    private DispatcherTimer? _pollTimer;

    /// <summary>前回検知したデスクトップ ID（差分検知用）</summary>
    private Guid? _lastKnownDesktopId;

    /// <summary>デスクトップ切替が検知された時に発火する（引数: 新しいデスクトップ ID）</summary>
    public event Action<Guid>? DesktopChanged;

    // ==========================================
    //  P3.5-1: COM 初期化
    // ==========================================

    /// <summary>
    /// IVirtualDesktopManager の COM 初期化
    /// 失敗時は IsAvailable=false で graceful 無効化する（アプリはクラッシュしない）
    /// ★ UI スレッドから呼ぶこと（STA COM のため）
    /// </summary>
    public bool Initialize()
    {
        try
        {
            var vdmType = Type.GetTypeFromCLSID(VirtualDesktopGuids.CLSID_VirtualDesktopManager);
            if (vdmType == null)
            {
                Log.Warning("[VD] VirtualDesktopManager の CLSID が解決できません");
                IsAvailable = false;
                return false;
            }

            var instance = Activator.CreateInstance(vdmType);
            _vdm = instance as IVirtualDesktopManager;
            IsAvailable = _vdm != null;

            if (IsAvailable)
            {
                Log.Information("[VD] IVirtualDesktopManager COM 初期化成功");
            }
            else
            {
                Log.Warning("[VD] IVirtualDesktopManager の QueryInterface に失敗");
            }

            return IsAvailable;
        }
        catch (COMException ex)
        {
            Log.Warning(ex, "[VD] COM 初期化失敗（COMException）— graceful 無効化");
            IsAvailable = false;
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[VD] COM 初期化失敗（予期しない例外）— graceful 無効化");
            IsAvailable = false;
            return false;
        }
    }

    // ==========================================
    //  P3.5-2: 現在デスクトップ ID 取得（短命ウィンドウ方式）
    // ==========================================

    /// <summary>
    /// 現在の仮想デスクトップ ID を取得する
    ///
    /// 方式: 短命ウィンドウを作成 → GetWindowDesktopId → GUID 取得 → 破棄
    /// 根拠: Microsoft Q&A で推奨されている回避策
    ///   Registry の CurrentVirtualDesktop は空になることがあるため、
    ///   実際にウィンドウを作って所属デスクトップ ID を取得する方が安定
    /// </summary>
    public Guid? GetCurrentDesktopId()
    {
        if (_vdm == null) return null;

        Window? tempWindow = null;
        try
        {
            // 短命ウィンドウ: 見えない位置、透明、フォーカスを奪わない
            tempWindow = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Opacity = 0,
                Left = -32000,
                Top = -32000,
            };

            tempWindow.Show();
            var hwnd = new WindowInteropHelper(tempWindow).Handle;
            var hr = _vdm.GetWindowDesktopId(hwnd, out var desktopId);
            tempWindow.Close();
            tempWindow = null;

            if (hr == 0 && desktopId != Guid.Empty)
            {
                Log.Information("[VD] 現在デスクトップ ID: {DesktopId}", desktopId);
                return desktopId;
            }

            Log.Warning("[VD] GetWindowDesktopId 失敗: HR=0x{HR:X8}, DesktopId={DesktopId}", hr, desktopId);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VD] 現在デスクトップ ID 取得例外");
            return null;
        }
        finally
        {
            // 万が一例外でも確実にクリーンアップ
            try { tempWindow?.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// 指定ウィンドウの所属デスクトップ ID を取得する
    /// </summary>
    public Guid? GetWindowDesktopId(IntPtr hwnd)
    {
        if (_vdm == null) return null;

        try
        {
            var hr = _vdm.GetWindowDesktopId(hwnd, out var desktopId);
            if (hr == 0 && desktopId != Guid.Empty)
            {
                return desktopId;
            }

            Log.Warning("[VD] GetWindowDesktopId 失敗: HR=0x{HR:X8}", hr);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VD] GetWindowDesktopId 例外");
            return null;
        }
    }

    // ==========================================
    //  P3.5-3: ウィンドウ移動
    // ==========================================

    /// <summary>
    /// 指定ウィンドウが現在の仮想デスクトップ上にあるか判定する
    /// </summary>
    public bool? IsWindowOnCurrentDesktop(IntPtr hwnd)
    {
        if (_vdm == null) return null;

        try
        {
            var hr = _vdm.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent);
            if (hr == 0) return onCurrent;

            Log.Warning("[VD] IsWindowOnCurrentVirtualDesktop 失敗: HR=0x{HR:X8}", hr);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VD] IsWindowOnCurrentVirtualDesktop 例外");
            return null;
        }
    }

    /// <summary>
    /// 指定ウィンドウを別の仮想デスクトップへ移動する
    /// </summary>
    public bool MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)
    {
        if (_vdm == null) return false;

        try
        {
            var hr = _vdm.MoveWindowToDesktop(hwnd, ref desktopId);
            if (hr == 0)
            {
                Log.Information("[VD] ウィンドウ移動成功: HWND=0x{Hwnd:X}, 移動先={DesktopId}",
                    hwnd.ToInt64(), desktopId);
                return true;
            }

            Log.Warning("[VD] MoveWindowToDesktop 失敗: HR=0x{HR:X8}, HWND=0x{Hwnd:X}, 移動先={DesktopId}",
                hr, hwnd.ToInt64(), desktopId);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VD] MoveWindowToDesktop 例外: HWND=0x{Hwnd:X}", hwnd.ToInt64());
            return false;
        }
    }

    // ==========================================
    //  P3.5-4: Registry からデスクトップ一覧取得
    // ==========================================

    /// <summary>
    /// Registry から仮想デスクトップ一覧を取得する（ベストエフォート）
    ///
    /// ★注意: デスクトップが1つだけの場合、VirtualDesktopIDs が空になることがある
    ///   （PowerToys issue #2160 で報告されている既知の挙動）
    /// </summary>
    public List<(Guid Id, string Name)> GetDesktopListFromRegistry()
    {
        var result = new List<(Guid Id, string Name)>();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(VD_REG_PATH);
            if (key == null)
            {
                Log.Warning("[VD] VirtualDesktops レジストリキーが見つかりません");
                return result;
            }

            var idsBytes = key.GetValue("VirtualDesktopIDs") as byte[];
            if (idsBytes == null || idsBytes.Length == 0)
            {
                Log.Information("[VD] VirtualDesktopIDs が空（デスクトップ1つのみ、または値なし）");
                return result;
            }

            if (idsBytes.Length % 16 != 0)
            {
                Log.Warning("[VD] VirtualDesktopIDs のサイズが不正: {Length} bytes（16の倍数でない）",
                    idsBytes.Length);
                return result;
            }

            // 16 bytes ごとに GUID をパース
            for (int i = 0; i + 16 <= idsBytes.Length; i += 16)
            {
                var guidBytes = new byte[16];
                Array.Copy(idsBytes, i, guidBytes, 0, 16);
                var guid = new Guid(guidBytes);

                var name = GetDesktopName(guid) ?? $"Desktop {result.Count + 1}";
                result.Add((guid, name));
            }

            Log.Information("[VD] Registry から {Count} 個のデスクトップを取得: [{Names}]",
                result.Count, string.Join(", ", result.Select(d => d.Name)));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VD] Registry デスクトップ一覧取得失敗");
        }

        return result;
    }

    /// <summary>
    /// 指定デスクトップの表示名を Registry から取得する
    /// </summary>
    private static string? GetDesktopName(Guid desktopId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"{VD_DESKTOPS_PATH}\{{{desktopId}}}");
            return key?.GetValue("Name") as string;
        }
        catch
        {
            return null;
        }
    }

    // ==========================================
    //  P8-6: デスクトップ喪失フォールバック
    // ==========================================

    /// <summary>
    /// 指定された DesktopId が現在のシステムに存在するか検証する（P8-6）
    /// 
    /// 検証方法:
    ///   1. Registry の VirtualDesktopIDs 一覧に含まれているか
    ///   2. 一覧が空（VD が1つだけ）の場合は「不明」→ 現在VDと比較
    /// 
    /// 戻り値: true=存在する / false=存在しない / null=判定不能（VD利用不可）
    /// </summary>
    public bool? IsDesktopAlive(Guid desktopId)
    {
        if (!IsAvailable) return null;
        if (desktopId == Guid.Empty) return false;

        // Registry から現在の VD 一覧を取得
        var desktops = GetDesktopListFromRegistry();

        if (desktops.Count > 0)
        {
            // 一覧がある場合: 含まれていれば存在する
            return desktops.Any(d => d.Id == desktopId);
        }

        // 一覧が空 = VD が1つだけ、または Registry が空
        // → 現在の VD ID と一致すれば存在する
        var currentId = GetCurrentDesktopIdFast();
        if (currentId.HasValue)
        {
            return desktopId == currentId.Value;
        }

        return null; // 判定不能
    }

    /// <summary>
    /// 複数の DesktopId を一括検証し、存在しないものを返す（P8-6: 起動時バッチ処理用）
    /// </summary>
    public HashSet<Guid> FindOrphanedDesktopIds(IEnumerable<Guid> desktopIds)
    {
        var orphaned = new HashSet<Guid>();
        if (!IsAvailable) return orphaned;

        // Registry から現在の VD 一覧を取得（1回だけ）
        var desktops = GetDesktopListFromRegistry();
        var currentId = GetCurrentDesktopIdFast();

        if (desktops.Count > 0)
        {
            var aliveSet = new HashSet<Guid>(desktops.Select(d => d.Id));
            foreach (var id in desktopIds)
            {
                if (id != Guid.Empty && !aliveSet.Contains(id))
                {
                    orphaned.Add(id);
                }
            }
        }
        else if (currentId.HasValue)
        {
            // VD 一覧が空（1つだけ）の場合: 現在 VD 以外は孤立とみなす
            foreach (var id in desktopIds)
            {
                if (id != Guid.Empty && id != currentId.Value)
                {
                    orphaned.Add(id);
                }
            }
        }
        // else: 判定不能 → 孤立なしとして扱う（安全側）

        if (orphaned.Count > 0)
        {
            Log.Information("[VD] 孤立デスクトップ検出: {Count}件 ({Ids})",
                orphaned.Count, string.Join(", ", orphaned));
        }

        return orphaned;
    }

    // ==========================================
    //  VD Tracker Window
    // ==========================================

    /// <summary>
    /// VD Tracker Window を初期化する
    /// WS_EX_TRANSPARENT を付けない常駐 HWND で、IsWindowOnCurrentVirtualDesktop による
    /// 高速チェックと、GetWindowDesktopId によるキャッシュ更新に使う。
    /// ★ UI スレッドから呼ぶこと
    /// </summary>
    public void InitializeTracker(IntPtr ownerHwnd)
    {
        if (_trackerWindow != null) return;

        _trackerWindow = new Window
        {
            Width = 1, Height = 1,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Opacity = 0,
            Left = -32000, Top = -32000,
            Title = "TopFusen_VDTracker"
        };

        // Alt+Tab 非表示のためにオーナーを設定
        if (ownerHwnd != IntPtr.Zero)
        {
            new WindowInteropHelper(_trackerWindow).Owner = ownerHwnd;
        }

        _trackerWindow.Show();
        _trackerHwnd = new WindowInteropHelper(_trackerWindow).Handle;

        // WS_EX_TRANSPARENT / WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW を絶対に付けない
        // （VD 追跡を壊さないため）
        var exStyle = NativeMethods.GetWindowLong(_trackerHwnd, NativeMethods.GWL_EXSTYLE);
        exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
        exStyle &= ~NativeMethods.WS_EX_NOACTIVATE;
        exStyle &= ~NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(_trackerHwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        // 初回の現在デスクトップ ID を取得してキャッシュ
        if (_vdm != null)
        {
            var hr = _vdm.GetWindowDesktopId(_trackerHwnd, out var desktopId);
            if (hr == 0 && desktopId != Guid.Empty)
            {
                _lastKnownDesktopId = desktopId;
            }
        }

        Log.Information("[VD] Tracker Window 初期化完了 (HWND=0x{Handle:X}, 初期VD={DesktopId})",
            _trackerHwnd.ToInt64(), _lastKnownDesktopId);
    }

    /// <summary>
    /// 現在のデスクトップ ID を高速取得する（DJ-10 方式）
    ///
    /// 高速パス: Tracker の IsWindowOnCurrentVirtualDesktop → true ならキャッシュを返す
    /// 中速パス: Registry の CurrentVirtualDesktop を読む
    /// 低速パス: 短命ウィンドウ方式（従来）
    /// </summary>
    public Guid? GetCurrentDesktopIdFast()
    {
        if (_vdm == null) return null;

        // 高速パス: Tracker が現在 VD にいるか → いるならキャッシュを返す
        if (_trackerHwnd != IntPtr.Zero && _lastKnownDesktopId.HasValue)
        {
            var isOnCurrent = IsWindowOnCurrentDesktop(_trackerHwnd);
            if (isOnCurrent == true)
            {
                return _lastKnownDesktopId; // 変化なし
            }
        }

        // 中速パス: Registry から現在 VD の GUID を読む
        var regId = GetCurrentDesktopIdFromRegistry();
        if (regId.HasValue)
        {
            _lastKnownDesktopId = regId;
            // Tracker を新しい VD に移動（次回の高速パスに備える）
            if (_trackerHwnd != IntPtr.Zero)
            {
                MoveWindowToDesktop(_trackerHwnd, regId.Value);
            }
            return regId;
        }

        // 低速パス: 短命ウィンドウ方式（フォールバック）
        var tempId = GetCurrentDesktopId();
        if (tempId.HasValue)
        {
            _lastKnownDesktopId = tempId;
            if (_trackerHwnd != IntPtr.Zero)
            {
                MoveWindowToDesktop(_trackerHwnd, tempId.Value);
            }
        }
        return tempId;
    }

    /// <summary>
    /// Registry の CurrentVirtualDesktop から現在のデスクトップ ID を読む
    /// ★ 値が空になることがある（デスクトップが1つのみの場合等）— その場合は null
    /// </summary>
    public Guid? GetCurrentDesktopIdFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(VD_REG_PATH);
            if (key == null) return null;

            var bytes = key.GetValue("CurrentVirtualDesktop") as byte[];
            if (bytes == null || bytes.Length != 16) return null;

            var guid = new Guid(bytes);
            return guid != Guid.Empty ? guid : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[VD] Registry CurrentVirtualDesktop 読み取り失敗");
            return null;
        }
    }

    // ==========================================
    //  P8.0-3: DWM Cloak / Uncloak
    // ==========================================

    /// <summary>
    /// ウィンドウを DWM Cloak で隠す（見えなくなるが DWM 合成は継続される）
    /// DJ-10: 現在の VD に属さない付箋を隠すために使用
    /// </summary>
    public static void CloakWindow(IntPtr hwnd)
    {
        int cloaked = 1;
        var hr = NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_CLOAK, ref cloaked, sizeof(int));
        if (hr != 0)
        {
            Log.Warning("[VD] Cloak 失敗: HR=0x{HR:X8}, HWND=0x{Hwnd:X}", hr, hwnd.ToInt64());
        }
    }

    /// <summary>
    /// ウィンドウの DWM Cloak を解除して再表示する
    /// Uncloak 後に Topmost を再主張する（Z 順が崩れる可能性への対策）
    /// </summary>
    public static void UncloakWindow(IntPtr hwnd)
    {
        int uncloaked = 0;
        var hr = NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_CLOAK, ref uncloaked, sizeof(int));
        if (hr != 0)
        {
            Log.Warning("[VD] Uncloak 失敗: HR=0x{HR:X8}, HWND=0x{Hwnd:X}", hr, hwnd.ToInt64());
        }

        // Topmost を再主張（Uncloak 後に Z 順が崩れることがあるため）
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    // ==========================================
    //  P8.0-4: デスクトップ監視（DispatcherTimer ポーリング）
    // ==========================================

    /// <summary>
    /// VD 切替検知のポーリングを開始する
    /// 一定間隔で現在のデスクトップ ID をチェックし、変化があれば DesktopChanged イベントを発火
    /// </summary>
    public void StartDesktopMonitoring(int intervalMs = 300)
    {
        if (_pollTimer != null) return;
        if (!IsAvailable) return;

        _lastKnownDesktopId = GetCurrentDesktopIdFast();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _pollTimer.Tick += OnPollTimerTick;
        _pollTimer.Start();

        Log.Information("[VD] デスクトップ監視開始（ポーリング {Interval}ms, 初期VD={DesktopId}）",
            intervalMs, _lastKnownDesktopId);
    }

    /// <summary>
    /// VD 切替検知のポーリングを停止する
    /// </summary>
    public void StopDesktopMonitoring()
    {
        if (_pollTimer == null) return;

        _pollTimer.Stop();
        _pollTimer.Tick -= OnPollTimerTick;
        _pollTimer = null;

        Log.Information("[VD] デスクトップ監視停止");
    }

    /// <summary>
    /// ポーリングタイマーの Tick ハンドラ
    /// 現在の VD ID を取得し、前回と異なる場合に DesktopChanged を発火
    /// </summary>
    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        if (_vdm == null || !IsAvailable) return;

        var previousId = _lastKnownDesktopId;
        var currentId = GetCurrentDesktopIdFast();
        if (currentId == null) return;

        if (previousId.HasValue && previousId.Value == currentId.Value)
            return; // 変化なし

        Log.Information("[VD] デスクトップ切替検知: {PreviousId} → {CurrentId}",
            previousId, currentId);

        DesktopChanged?.Invoke(currentId.Value);
    }

    // ==========================================
    //  クリーンアップ
    // ==========================================

    public void Dispose()
    {
        StopDesktopMonitoring();

        if (_trackerWindow != null)
        {
            try { _trackerWindow.Close(); } catch { /* ignore */ }
            _trackerWindow = null;
            _trackerHwnd = IntPtr.Zero;
            Log.Information("[VD] Tracker Window 閉じた");
        }

        if (_vdm != null)
        {
            Marshal.ReleaseComObject(_vdm);
            _vdm = null;
            Log.Information("[VD] COM オブジェクト解放");
        }
        IsAvailable = false;
    }
}
