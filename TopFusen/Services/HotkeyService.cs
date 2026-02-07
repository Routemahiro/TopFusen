using System.Windows.Interop;
using Serilog;
using TopFusen.Interop;
using TopFusen.Models;

namespace TopFusen.Services;

/// <summary>
/// グローバルホットキー管理サービス（Phase 10: FR-HOTKEY）
/// - RegisterHotKey / UnregisterHotKey で OS レベルのホットキーを管理
/// - WM_HOTKEY メッセージをフックして HotkeyPressed イベントを発火
/// - 登録失敗時のエラー通知
/// - 既定: Ctrl+Win+E → 編集モードトグル
/// </summary>
public class HotkeyService : IDisposable
{
    /// <summary>ホットキー ID（RegisterHotKey の id パラメータ）</summary>
    private const int HOTKEY_ID_EDIT_TOGGLE = 0x0001;

    /// <summary>フック先ウィンドウの HWND</summary>
    private IntPtr _hookHwnd;

    /// <summary>HwndSource（メッセージフック管理）</summary>
    private HwndSource? _hwndSource;

    /// <summary>現在のホットキー設定</summary>
    private HotkeySettings _settings = new();

    /// <summary>ホットキーが現在登録済みかどうか</summary>
    public bool IsRegistered { get; private set; }

    /// <summary>最後の登録エラーメッセージ（null = エラーなし）</summary>
    public string? LastError { get; private set; }

    private bool _isDisposed;

    /// <summary>ホットキーが押された時のイベント</summary>
    public event Action? HotkeyPressed;

    /// <summary>
    /// ホットキーサービスを初期化する
    /// </summary>
    /// <param name="hookHwnd">WM_HOTKEY を受け取るウィンドウの HWND（オーナーウィンドウ）</param>
    /// <param name="settings">ホットキー設定</param>
    public void Initialize(IntPtr hookHwnd, HotkeySettings settings)
    {
        _hookHwnd = hookHwnd;
        _settings = settings;

        // HwndSource を取得してメッセージフックを登録
        _hwndSource = HwndSource.FromHwnd(hookHwnd);
        if (_hwndSource == null)
        {
            Log.Warning("HotkeyService: HwndSource の取得に失敗しました (HWND=0x{Handle:X8})", hookHwnd);
            return;
        }

        _hwndSource.AddHook(WndProc);

        // 設定が有効ならホットキーを登録
        if (settings.Enabled)
        {
            Register();
        }
        else
        {
            Log.Information("ホットキー: 無効（設定による）");
        }
    }

    /// <summary>
    /// ホットキーを登録する
    /// </summary>
    public bool Register()
    {
        if (_hookHwnd == IntPtr.Zero) return false;

        // 既存の登録を解除
        Unregister();

        var success = NativeMethods.RegisterHotKey(
            _hookHwnd,
            HOTKEY_ID_EDIT_TOGGLE,
            (uint)_settings.Modifiers | NativeMethods.MOD_NOREPEAT,
            (uint)_settings.Key);

        if (success)
        {
            IsRegistered = true;
            LastError = null;
            Log.Information("ホットキー登録成功: Modifiers=0x{Mod:X4}, Key=0x{Key:X2}",
                _settings.Modifiers, _settings.Key);
        }
        else
        {
            IsRegistered = false;
            var errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            LastError = $"ホットキーの登録に失敗しました（エラーコード: {errorCode}）。\n他のアプリがこのキーを使用している可能性があります。";
            Log.Warning("ホットキー登録失敗: Modifiers=0x{Mod:X4}, Key=0x{Key:X2}, Error={Error}",
                _settings.Modifiers, _settings.Key, errorCode);
        }

        return success;
    }

    /// <summary>
    /// ホットキーの登録を解除する
    /// </summary>
    public void Unregister()
    {
        if (_hookHwnd != IntPtr.Zero && IsRegistered)
        {
            NativeMethods.UnregisterHotKey(_hookHwnd, HOTKEY_ID_EDIT_TOGGLE);
            IsRegistered = false;
            Log.Information("ホットキー登録解除");
        }
    }

    /// <summary>
    /// 設定を更新する（有効/無効の切り替え）
    /// </summary>
    public void UpdateSettings(HotkeySettings newSettings)
    {
        _settings = newSettings;

        if (newSettings.Enabled)
        {
            Register();
        }
        else
        {
            Unregister();
            LastError = null;
        }
    }

    /// <summary>
    /// WM_HOTKEY メッセージフック
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_EDIT_TOGGLE)
        {
            handled = true;
            Log.Information("ホットキー検知: 編集モードトグル");
            HotkeyPressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Unregister();

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }
}
