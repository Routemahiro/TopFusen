using System.Runtime.InteropServices;

namespace TopFusen.Interop;

/// <summary>
/// Win32 API の P/Invoke 定義（最小限）
/// Phase 1: WS_EX_TOOLWINDOW（Alt+Tab 非表示）
/// Phase 2: WS_EX_TRANSPARENT, WS_EX_NOACTIVATE 等を追加予定
/// </summary>
internal static class NativeMethods
{
    // ----- Window Extended Style index -----
    internal const int GWL_EXSTYLE = -20;

    // ----- Extended Window Styles -----
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_APPWINDOW  = 0x00040000;

    // Phase 2 で追加予定:
    // internal const int WS_EX_TRANSPARENT = 0x00000020;
    // internal const int WS_EX_LAYERED     = 0x00080000;
    // internal const int WS_EX_NOACTIVATE  = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
