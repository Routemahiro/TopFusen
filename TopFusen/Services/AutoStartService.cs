using Microsoft.Win32;
using Serilog;

namespace TopFusen.Services;

/// <summary>
/// 自動起動管理（Phase 10: FR-BOOT-1）
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run にアプリを登録/解除する
/// ユーザー権限で動作（管理者不要）
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "TopFusen";

    /// <summary>
    /// 自動起動が現在有効かどうかを取得する（レジストリの実際の状態）
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "自動起動状態の確認に失敗しました");
            return false;
        }
    }

    /// <summary>
    /// 自動起動を有効にする（レジストリに登録）
    /// </summary>
    public static bool Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                Log.Warning("自動起動登録失敗: 実行ファイルパスが取得できません");
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null)
            {
                Log.Warning("自動起動登録失敗: レジストリキーを開けません");
                return false;
            }

            // クォート付きパス + --autostart フラグ
            var value = $"\"{exePath}\" --autostart";
            key.SetValue(AppName, value, RegistryValueKind.String);

            Log.Information("自動起動を登録しました: {Value}", value);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "自動起動の登録に失敗しました");
            return false;
        }
    }

    /// <summary>
    /// 自動起動を無効にする（レジストリから削除）
    /// </summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return true; // キーが無い = 登録されていない

            key.DeleteValue(AppName, false); // throwOnMissingValue=false
            Log.Information("自動起動を解除しました");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "自動起動の解除に失敗しました");
            return false;
        }
    }

    /// <summary>
    /// 自動起動の有効/無効を設定する
    /// </summary>
    public static bool SetEnabled(bool enabled)
    {
        return enabled ? Enable() : Disable();
    }
}
