using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Serilog;
using TopFusen.Models;

namespace TopFusen.Services;

/// <summary>
/// 永続化サービス（Phase 5: FR-PERSIST）
/// - JSON ファイルの読み書き（notes.json / settings.json）
/// - RTF ファイルの読み書き
/// - Atomic Write（tmp → File.Replace → .bak 生成）
/// - 破損検知 + .bak フォールバック
/// - デバウンス保存（3秒 DispatcherTimer — UIスレッドで発火）
/// - 孤立RTFファイルの自動削除
/// </summary>
public class PersistenceService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>デバウンスタイマー（UIスレッドで発火 — RichTextBox アクセスに必要）</summary>
    private readonly DispatcherTimer _debounceTimer;

    private bool _isDisposed;

    /// <summary>保存処理をリクエストするイベント（デバウンスタイマー発火時、UIスレッド上）</summary>
    public event Action? SaveRequested;

    /// <summary>破損からバックアップ復旧が発生したか（起動時チェック用）</summary>
    public bool CorruptionRecovered { get; private set; }

    /// <summary>復旧時のメッセージ（ユーザー通知用）</summary>
    public string? RecoveryMessage { get; private set; }

    public PersistenceService()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _debounceTimer.Tick += OnDebounceTick;
    }

    // ==========================================
    //  デバウンス保存
    // ==========================================

    /// <summary>
    /// 保存をスケジュールする（3秒デバウンス）
    /// 連続呼び出し時はタイマーをリセットして最後の呼び出しから3秒後に保存
    /// </summary>
    public void ScheduleSave()
    {
        if (_isDisposed) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    /// <summary>
    /// 即座に保存を実行する（終了時・SessionEnding 時用）
    /// </summary>
    public void FlushSave()
    {
        _debounceTimer.Stop();
        try
        {
            SaveRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FlushSave 中にエラーが発生しました");
        }
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        try
        {
            SaveRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "デバウンス保存中にエラーが発生しました");
        }
    }

    // ==========================================
    //  JSON 読み書き（notes.json / settings.json）
    // ==========================================

    /// <summary>
    /// notes.json を読み込む（破損時は .bak にフォールバック）
    /// </summary>
    public NotesData? LoadNotesData()
    {
        return LoadJsonWithFallback<NotesData>(
            AppDataPaths.NotesJson, AppDataPaths.NotesJsonBak, "notes.json");
    }

    /// <summary>
    /// notes.json を Atomic Write で保存する
    /// </summary>
    public void SaveNotesData(NotesData data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        AtomicWrite(AppDataPaths.NotesJson, AppDataPaths.NotesJsonBak, json);
        Log.Debug("notes.json 保存完了（付箋数: {Count}）", data.Notes.Count);
    }

    /// <summary>
    /// settings.json を読み込む（破損時は .bak にフォールバック）
    /// </summary>
    public AppSettings? LoadSettings()
    {
        return LoadJsonWithFallback<AppSettings>(
            AppDataPaths.SettingsJson, AppDataPaths.SettingsJsonBak, "settings.json");
    }

    /// <summary>
    /// settings.json を Atomic Write で保存する
    /// </summary>
    public void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        AtomicWrite(AppDataPaths.SettingsJson, AppDataPaths.SettingsJsonBak, json);
        Log.Debug("settings.json 保存完了");
    }

    // ==========================================
    //  RTF ファイル読み書き
    // ==========================================

    /// <summary>
    /// RTF ファイルを読み込む（バイト配列として）
    /// </summary>
    public byte[]? LoadRtf(Guid noteId)
    {
        var path = AppDataPaths.GetRtfPath(noteId);
        if (!File.Exists(path)) return null;

        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RTF ファイル読み込み失敗: {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// RTF ファイルを保存する（バイト配列）
    /// </summary>
    public void SaveRtf(Guid noteId, byte[] rtfContent)
    {
        var path = AppDataPaths.GetRtfPath(noteId);
        try
        {
            File.WriteAllBytes(path, rtfContent);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RTF ファイル保存失敗: {Path}", path);
        }
    }

    /// <summary>
    /// RTF ファイルを削除する
    /// </summary>
    public void DeleteRtf(Guid noteId)
    {
        var path = AppDataPaths.GetRtfPath(noteId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Log.Information("RTF ファイル削除: {NoteId}", noteId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RTF ファイル削除失敗: {NoteId}", noteId);
        }
    }

    /// <summary>
    /// 孤立 RTF ファイル（notes.json に対応するエントリがないもの）を削除する
    /// </summary>
    public void CleanupOrphanedRtfFiles(ISet<Guid> validNoteIds)
    {
        var notesDir = AppDataPaths.NotesDirectory;
        if (!Directory.Exists(notesDir)) return;

        var rtfFiles = Directory.GetFiles(notesDir, "*.rtf");
        int cleaned = 0;

        foreach (var file in rtfFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (Guid.TryParse(fileName, out var id) && !validNoteIds.Contains(id))
            {
                try
                {
                    File.Delete(file);
                    cleaned++;
                    Log.Information("孤立 RTF ファイルを削除: {FileName}", fileName);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "孤立 RTF ファイル削除失敗: {FileName}", fileName);
                }
            }
        }

        if (cleaned > 0)
        {
            Log.Information("孤立 RTF ファイル掃除完了（{Count}件削除）", cleaned);
        }
    }

    // ==========================================
    //  内部ヘルパー
    // ==========================================

    /// <summary>
    /// Atomic Write: tmp に書き込み → File.Replace で本体に置換（.bak を自動生成）
    /// File.Replace は「tmp → 本体 に置換 + 旧本体 → .bak に移動」を1アトミック操作で行う
    /// </summary>
    private static void AtomicWrite(string filePath, string bakPath, string content)
    {
        var tmpPath = filePath + ".tmp";

        try
        {
            // 1. tmp ファイルに書き込み
            File.WriteAllText(tmpPath, content, Encoding.UTF8);

            // 2. 本体が存在する場合は File.Replace（旧本体→.bak + tmp→本体）
            if (File.Exists(filePath))
            {
                File.Replace(tmpPath, filePath, bakPath);
            }
            else
            {
                // 初回保存: tmp を本体にリネーム
                File.Move(tmpPath, filePath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Atomic Write 失敗: {FilePath}", filePath);

            // フォールバック: tmp が残っていたら直接コピー
            try
            {
                if (File.Exists(tmpPath))
                {
                    File.Copy(tmpPath, filePath, overwrite: true);
                    File.Delete(tmpPath);
                    Log.Warning("Atomic Write フォールバック成功（直接コピー）: {FilePath}", filePath);
                }
            }
            catch (Exception ex2)
            {
                Log.Error(ex2, "Atomic Write フォールバックも失敗: {FilePath}", filePath);
            }
        }
    }

    /// <summary>
    /// JSON ファイルを読み込む（破損時は .bak にフォールバック）
    /// </summary>
    private T? LoadJsonWithFallback<T>(string mainPath, string bakPath, string displayName) where T : class
    {
        // 1. メインファイルから読み込み
        if (File.Exists(mainPath))
        {
            try
            {
                var json = File.ReadAllText(mainPath, Encoding.UTF8);
                var data = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (data != null)
                {
                    Log.Information("{File} 読み込み成功", displayName);
                    return data;
                }
                Log.Warning("{File} のデシリアライズ結果が null", displayName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "{File} の読み込み/パースに失敗。バックアップを試みます", displayName);
            }
        }

        // 2. バックアップからフォールバック
        if (File.Exists(bakPath))
        {
            try
            {
                var json = File.ReadAllText(bakPath, Encoding.UTF8);
                var data = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (data != null)
                {
                    Log.Warning("{File} をバックアップから復旧しました", displayName);
                    CorruptionRecovered = true;
                    RecoveryMessage = (RecoveryMessage == null)
                        ? $"{displayName} が破損していたため、バックアップから復旧しました。"
                        : RecoveryMessage + $"\n{displayName} が破損していたため、バックアップから復旧しました。";
                    return data;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "{File} のバックアップも読み込み/パースに失敗", displayName);
            }
        }

        // 3. 両方失敗または存在しない → null（初回起動時はファイルが無い）
        Log.Information("{File} が見つかりません（初回起動の可能性あり）", displayName);
        return null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _debounceTimer.Stop();
    }
}
