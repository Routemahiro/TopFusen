using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Serilog;
using TopFusen.Interop;

namespace TopFusen.Services;

/// <summary>
/// 仮想デスクトップ操作サービス
///
/// Phase 3.5: 技術スパイク（API 成立検証）
/// Phase 8: 本格実装に昇格予定
///
/// DJ-4: COM 呼び出しは UI スレッド（STA）から行う
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
    //  クリーンアップ
    // ==========================================

    public void Dispose()
    {
        if (_vdm != null)
        {
            Marshal.ReleaseComObject(_vdm);
            _vdm = null;
            Log.Information("[VD] COM オブジェクト解放");
        }
        IsAvailable = false;
    }
}
