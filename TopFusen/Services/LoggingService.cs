using System.IO;
using System.Text;
using Serilog;
using Serilog.Events;

namespace TopFusen.Services;

/// <summary>
/// ログ基盤の初期化と管理
/// 出力先: %LocalAppData%\TopFusen\TopFusen\logs\app_yyyyMMdd.log
/// ローテーション: 7日分保持
/// </summary>
public static class LoggingService
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TopFusen", "TopFusen", "logs");

    /// <summary>
    /// Serilog のグローバルロガーを初期化する
    /// </summary>
    public static void Initialize()
    {
        // ログディレクトリがなければ作成
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                path: Path.Combine(LogDirectory, "app_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                encoding: Encoding.UTF8,
                shared: true)
            .CreateLogger();

        Log.Information("=== TopFusen 起動 ===");
        Log.Information("OS: {OS}", Environment.OSVersion);
        Log.Information("ログ出力先: {LogDir}", LogDirectory);
    }

    /// <summary>
    /// ログをフラッシュして閉じる
    /// </summary>
    public static void Shutdown()
    {
        Log.Information("=== TopFusen 終了 ===");
        Log.CloseAndFlush();
    }

    /// <summary>
    /// ログディレクトリのパスを取得する（診断パッケージ用）
    /// </summary>
    public static string GetLogDirectory() => LogDirectory;
}
