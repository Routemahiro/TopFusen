using System.Runtime.InteropServices;

namespace TopFusen.Interop;

/// <summary>
/// Win32 API の P/Invoke 定義
/// Phase 1: WS_EX_TOOLWINDOW（Alt+Tab 非表示）
/// Phase 2: WS_EX_TRANSPARENT, WS_EX_NOACTIVATE（クリック透過 + 非アクティブ化）
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
