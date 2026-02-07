using System.Runtime.InteropServices;

namespace TopFusen.Interop;

/// <summary>
/// Win32 API の P/Invoke 定義
/// Phase 1: WS_EX_TOOLWINDOW（Alt+Tab 非表示）
/// Phase 2: WS_EX_TRANSPARENT, WS_EX_NOACTIVATE（クリック透過 + 非アクティブ化）
/// Phase 8: DWM Cloak + SetWindowPos（VD 自前管理用）
/// </summary>
internal static class NativeMethods
{
    // ----- Window Extended Style index -----
    internal const int GWL_EXSTYLE = -20;

    // ----- Extended Window Styles -----
    internal const int WS_EX_TRANSPARENT = 0x00000020;  // クリック透過（マウスイベントが背後へ通過）
    internal const int WS_EX_TOOLWINDOW  = 0x00000080;  // Alt+Tab 非表示
    internal const int WS_EX_APPWINDOW   = 0x00040000;  // タスクバー表示（除去用）
    internal const int WS_EX_LAYERED     = 0x00080000;  // レイヤードウィンドウ（AllowsTransparency で WPF が自動付与）
    internal const int WS_EX_NOACTIVATE  = 0x08000000;  // フォーカスを奪わない

    // ----- GetWindowLong / SetWindowLong -----

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ----- DWM Cloak（DJ-10: VD 自前管理 — ウィンドウの表示/非表示制御）-----

    /// <summary>
    /// DWM ウィンドウ属性を設定する
    /// DJ-10: DWMWA_CLOAK で Cloak/Uncloak を制御
    /// </summary>
    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>DWMWA_CLOAK: ウィンドウを隠す（DWM 合成は継続）</summary>
    internal const int DWMWA_CLOAK = 13;

    // ----- SetWindowPos（Topmost 再主張用）-----

    /// <summary>
    /// ウィンドウの位置・サイズ・Z 順を設定する
    /// DJ-10: Uncloak 後に Topmost を再主張するために使用
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    internal const uint SWP_NOMOVE     = 0x0002;
    internal const uint SWP_NOSIZE     = 0x0001;
    internal const uint SWP_NOACTIVATE = 0x0010;

    // ----- Phase 10: ホットキー（FR-HOTKEY）-----

    /// <summary>
    /// グローバルホットキーを登録する
    /// 既定: Ctrl+Win+E → 編集モードトグル
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>
    /// グローバルホットキーの登録を解除する
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>WM_HOTKEY メッセージ</summary>
    internal const int WM_HOTKEY = 0x0312;

    // Modifier keys for RegisterHotKey
    internal const uint MOD_ALT     = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT   = 0x0004;
    internal const uint MOD_WIN     = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;
}
