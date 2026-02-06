using System.IO;

namespace TopFusen.Services;

/// <summary>
/// アプリケーションデータの保存パスを一元管理する
/// ベースパス: %LocalAppData%\TopFusen\TopFusen\
/// </summary>
public static class AppDataPaths
{
    private static readonly string BasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TopFusen", "TopFusen");

    /// <summary>ベースディレクトリ</summary>
    public static string Base => BasePath;

    /// <summary>settings.json のパス</summary>
    public static string SettingsJson => Path.Combine(BasePath, "settings.json");

    /// <summary>settings.json.bak のパス</summary>
    public static string SettingsJsonBak => Path.Combine(BasePath, "settings.json.bak");

    /// <summary>notes.json のパス</summary>
    public static string NotesJson => Path.Combine(BasePath, "notes.json");

    /// <summary>notes.json.bak のパス</summary>
    public static string NotesJsonBak => Path.Combine(BasePath, "notes.json.bak");

    /// <summary>notes ディレクトリ（RTFファイル格納）</summary>
    public static string NotesDirectory => Path.Combine(BasePath, "notes");

    /// <summary>logs ディレクトリ</summary>
    public static string LogsDirectory => Path.Combine(BasePath, "logs");

    /// <summary>
    /// 必要なディレクトリを全て作成する
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(BasePath);
        Directory.CreateDirectory(NotesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// 指定した NoteId の RTF ファイルパスを取得する
    /// </summary>
    public static string GetRtfPath(Guid noteId)
        => Path.Combine(NotesDirectory, $"{noteId}.rtf");
}
