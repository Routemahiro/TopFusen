using System.Runtime.InteropServices;

namespace TopFusen.Interop;

/// <summary>
/// IVirtualDesktopManager COM インターフェース定義
/// https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager
///
/// Phase 3.5: 技術スパイク用 COM 定義
/// Phase 8: 本格実装でもそのまま使用
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
internal interface IVirtualDesktopManager
{
    /// <summary>
    /// 指定ウィンドウが現在の仮想デスクトップ上にあるか判定する
    /// </summary>
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(
        IntPtr topLevelWindow,
        [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

    /// <summary>
    /// 指定ウィンドウが所属する仮想デスクトップの GUID を取得する
    /// </summary>
    [PreserveSig]
    int GetWindowDesktopId(
        IntPtr topLevelWindow,
        out Guid desktopId);

    /// <summary>
    /// 指定ウィンドウを別の仮想デスクトップへ移動する
    /// </summary>
    [PreserveSig]
    int MoveWindowToDesktop(
        IntPtr topLevelWindow,
        ref Guid desktopId);
}

/// <summary>
/// 仮想デスクトップ関連の CLSID / GUID 定数
/// </summary>
internal static class VirtualDesktopGuids
{
    /// <summary>CLSID_VirtualDesktopManager（CoCreateInstance 用）</summary>
    internal static readonly Guid CLSID_VirtualDesktopManager
        = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");
}
