using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;
using TopFusen.Models;
using TopFusen.Services;

namespace TopFusen.Views;

/// <summary>
/// 設定ウィンドウ（Phase 11: 案B — 4タブ構成）
/// 一般 / フォント / 付箋管理 / 詳細
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly NoteManager _noteManager;
    private readonly HotkeyService _hotkeyService;
    private readonly PersistenceService _persistence;

    // ==========================================
    //  ホットキープリセット定義
    // ==========================================

    private static readonly HotkeyPreset[] _presets = new[]
    {
        new HotkeyPreset("Ctrl+Shift+Alt+E",  0x0007, 0x45),  // MOD_ALT|CTRL|SHIFT + E
        new HotkeyPreset("Ctrl+Shift+F12",    0x0006, 0x7B),  // MOD_CTRL|SHIFT + F12
        new HotkeyPreset("Ctrl+Alt+F11",      0x0003, 0x7A),  // MOD_ALT|CTRL + F11
        new HotkeyPreset("Ctrl+Win+N",        0x000A, 0x4E),  // MOD_CTRL|WIN + N
        new HotkeyPreset("Ctrl+Shift+F9",     0x0006, 0x78),  // MOD_CTRL|SHIFT + F9
    };

    /// <summary>Phase 14: 非表示ホットキーのプリセット</summary>
    private static readonly HotkeyPreset[] _hidePresets = new[]
    {
        new HotkeyPreset("Ctrl+Shift+Alt+H",  0x0007, 0x48),  // MOD_ALT|CTRL|SHIFT + H
        new HotkeyPreset("Ctrl+Shift+F11",    0x0006, 0x7A),  // MOD_CTRL|SHIFT + F11
        new HotkeyPreset("Ctrl+Alt+F10",      0x0003, 0x79),  // MOD_ALT|CTRL + F10
        new HotkeyPreset("Ctrl+Win+H",        0x000A, 0x48),  // MOD_CTRL|WIN + H
        new HotkeyPreset("Ctrl+Shift+F8",     0x0006, 0x77),  // MOD_CTRL|SHIFT + F8
    };

    // ==========================================
    //  Z順管理用（ZOrderWindow と同等）
    // ==========================================

    private Guid _desktopId;
    private readonly ObservableCollection<ZOrderItem> _zOrderItems = new();
    private bool _isProcessingZOrderChange;

    /// <summary>初期化中フラグ（イベント発火を抑制）</summary>
    private bool _isInitializing = true;

    // ==========================================
    //  コンストラクタ
    // ==========================================

    public SettingsWindow(NoteManager noteManager, HotkeyService hotkeyService, PersistenceService persistence)
    {
        _noteManager = noteManager;
        _hotkeyService = hotkeyService;
        _persistence = persistence;

        InitializeComponent();

        InitializeGeneralTab();
        InitializeFontTab();
        InitializeZOrderTab();
        InitializeDetailsTab();

        _isInitializing = false;
    }

    // ==========================================
    //  一般タブ
    // ==========================================

    private void InitializeGeneralTab()
    {
        // 自動起動
        AutoStartCheckBox.IsChecked = AutoStartService.IsEnabled();

        // ホットキー
        var hotkey = _noteManager.AppSettings.Hotkey;
        HotkeyEnabledCheckBox.IsChecked = hotkey.Enabled;

        // プリセットコンボボックス
        foreach (var preset in _presets)
        {
            HotkeyPresetComboBox.Items.Add(preset.Label);
        }

        // 現在の設定に合うプリセットを選択
        var currentIndex = FindPresetIndex(_presets, hotkey.Modifiers, hotkey.Key);
        HotkeyPresetComboBox.SelectedIndex = currentIndex >= 0 ? currentIndex : 0;

        // ホットキー無効時はプリセットパネルを非表示
        HotkeyPresetPanel.IsEnabled = hotkey.Enabled;

        UpdateHotkeyStatusText();

        // Phase 14: 非表示ホットキー
        var hideHotkey = _noteManager.AppSettings.HideHotkey;
        HideHotkeyEnabledCheckBox.IsChecked = hideHotkey.Enabled;

        foreach (var preset in _hidePresets)
        {
            HideHotkeyPresetComboBox.Items.Add(preset.Label);
        }

        var hideIndex = FindPresetIndex(_hidePresets, hideHotkey.Modifiers, hideHotkey.Key);
        HideHotkeyPresetComboBox.SelectedIndex = hideIndex >= 0 ? hideIndex : 0;
        HideHotkeyPresetPanel.IsEnabled = hideHotkey.Enabled;

        UpdateHideHotkeyStatusText();
    }

    private static int FindPresetIndex(HotkeyPreset[] presets, int modifiers, int key)
    {
        for (int i = 0; i < presets.Length; i++)
        {
            if (presets[i].Modifiers == modifiers && presets[i].Key == key)
                return i;
        }
        return -1;
    }

    private void UpdateHotkeyStatusText()
    {
        if (!_hotkeyService.IsRegistered && _noteManager.AppSettings.Hotkey.Enabled)
        {
            HotkeyStatusText.Text = _hotkeyService.LastError ?? "ホットキー登録に失敗しました";
            HotkeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
        }
        else if (_noteManager.AppSettings.Hotkey.Enabled)
        {
            HotkeyStatusText.Text = "✓ ホットキーは正常に登録されています";
            HotkeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0x33));
        }
        else
        {
            HotkeyStatusText.Text = "ホットキーは無効です";
            HotkeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var enabled = AutoStartCheckBox.IsChecked == true;
        if (AutoStartService.SetEnabled(enabled))
        {
            _noteManager.AppSettings.AutoStartEnabled = enabled;
            _persistence.ScheduleSave();
            Log.Information("設定画面: 自動起動 = {Enabled}", enabled);
        }
        else
        {
            // 失敗したら元に戻す
            AutoStartCheckBox.IsChecked = !enabled;
        }
    }

    private void HotkeyEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var enabled = HotkeyEnabledCheckBox.IsChecked == true;
        _noteManager.AppSettings.Hotkey.Enabled = enabled;
        _hotkeyService.UpdateSettings(_noteManager.AppSettings.Hotkey);
        HotkeyPresetPanel.IsEnabled = enabled;
        _persistence.ScheduleSave();
        UpdateHotkeyStatusText();
        Log.Information("設定画面: ホットキー有効 = {Enabled}", enabled);
    }

    private void HotkeyPresetComboBox_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        var idx = HotkeyPresetComboBox.SelectedIndex;
        if (idx < 0 || idx >= _presets.Length) return;

        var preset = _presets[idx];
        _noteManager.AppSettings.Hotkey.Modifiers = preset.Modifiers;
        _noteManager.AppSettings.Hotkey.Key = preset.Key;

        if (_noteManager.AppSettings.Hotkey.Enabled)
        {
            _hotkeyService.UpdateSettings(_noteManager.AppSettings.Hotkey);
        }

        _persistence.ScheduleSave();
        UpdateHotkeyStatusText();
        Log.Information("設定画面: ホットキープリセット変更（編集）→ {Label}", preset.Label);
    }

    // ==========================================
    //  Phase 14: 非表示ホットキー
    // ==========================================

    private void UpdateHideHotkeyStatusText()
    {
        if (!_hotkeyService.IsHideRegistered && _noteManager.AppSettings.HideHotkey.Enabled)
        {
            HideHotkeyStatusText.Text = _hotkeyService.HideLastError ?? "非表示ホットキー登録に失敗しました";
            HideHotkeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
        }
        else if (_noteManager.AppSettings.HideHotkey.Enabled)
        {
            HideHotkeyStatusText.Text = "✓ 非表示ホットキーは正常に登録されています";
            HideHotkeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0x33));
        }
        else
        {
            HideHotkeyStatusText.Text = "非表示ホットキーは無効です";
            HideHotkeyStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }
    }

    private void HideHotkeyEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var enabled = HideHotkeyEnabledCheckBox.IsChecked == true;
        _noteManager.AppSettings.HideHotkey.Enabled = enabled;
        _hotkeyService.UpdateHideSettings(_noteManager.AppSettings.HideHotkey);
        HideHotkeyPresetPanel.IsEnabled = enabled;
        _persistence.ScheduleSave();
        UpdateHideHotkeyStatusText();
        Log.Information("設定画面: 非表示ホットキー有効 = {Enabled}", enabled);
    }

    private void HideHotkeyPresetComboBox_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        var idx = HideHotkeyPresetComboBox.SelectedIndex;
        if (idx < 0 || idx >= _hidePresets.Length) return;

        var preset = _hidePresets[idx];
        _noteManager.AppSettings.HideHotkey.Modifiers = preset.Modifiers;
        _noteManager.AppSettings.HideHotkey.Key = preset.Key;

        if (_noteManager.AppSettings.HideHotkey.Enabled)
        {
            _hotkeyService.UpdateHideSettings(_noteManager.AppSettings.HideHotkey);
        }

        _persistence.ScheduleSave();
        UpdateHideHotkeyStatusText();
        Log.Information("設定画面: ホットキープリセット変更（非表示）→ {Label}", preset.Label);
    }

    // ==========================================
    //  フォントタブ
    // ==========================================

    private void InitializeFontTab()
    {
        // 現在の許可リストを表示
        RefreshFontList();

        // システムフォント一覧をコンボボックスに追加
        var systemFonts = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(f => f)
            .ToList();

        foreach (var font in systemFonts)
        {
            FontAddComboBox.Items.Add(font);
        }
    }

    private void RefreshFontList()
    {
        FontListBox.Items.Clear();
        foreach (var font in _noteManager.AppSettings.FontAllowList)
        {
            FontListBox.Items.Add(font);
        }
    }

    private void FontAddButton_Click(object sender, RoutedEventArgs e)
    {
        var fontName = FontAddComboBox.Text?.Trim();
        if (string.IsNullOrEmpty(fontName)) return;

        if (_noteManager.AppSettings.FontAllowList.Contains(fontName))
        {
            MessageBox.Show($"「{fontName}」は既に許可リストに含まれています。",
                "TopFusen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _noteManager.AppSettings.FontAllowList.Add(fontName);
        RefreshFontList();
        _persistence.ScheduleSave();
        Log.Information("設定画面: フォント追加 = {Font}", fontName);

        // 全付箋にフォント許可リストを更新
        foreach (var window in _noteManager.Windows)
        {
            window.SetFontAllowList(_noteManager.AppSettings.FontAllowList);
        }
    }

    private void FontRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = FontListBox.SelectedItem as string;
        if (string.IsNullOrEmpty(selected)) return;

        _noteManager.AppSettings.FontAllowList.Remove(selected);
        RefreshFontList();
        _persistence.ScheduleSave();
        Log.Information("設定画面: フォント削除 = {Font}", selected);

        // 全付箋にフォント許可リストを更新
        foreach (var window in _noteManager.Windows)
        {
            window.SetFontAllowList(_noteManager.AppSettings.FontAllowList);
        }
    }

    // ==========================================
    //  付箋管理タブ（Z順 — ZOrderWindow と同等）
    // ==========================================

    private void InitializeZOrderTab()
    {
        _desktopId = _noteManager.GetCurrentDesktopId() ?? Guid.Empty;

        var desktopName = _desktopId != Guid.Empty
            ? _noteManager.GetDesktopName(_desktopId)
            : "デスクトップ";
        ZOrderDesktopText.Text = $"現在のデスクトップ: {desktopName}";

        PopulateZOrderList();

        _zOrderItems.CollectionChanged += OnZOrderItemsCollectionChanged;
        ZOrderListBox.ItemsSource = _zOrderItems;
    }

    private void PopulateZOrderList()
    {
        _isProcessingZOrderChange = true;
        try
        {
            _zOrderItems.Clear();
            var orderedNotes = _noteManager.GetOrderedNotesForDesktop(_desktopId);

            foreach (var (noteId, preview, bgHex) in orderedNotes)
            {
                Color bgColor;
                try
                {
                    bgColor = (Color)ColorConverter.ConvertFromString(bgHex);
                }
                catch
                {
                    bgColor = Color.FromRgb(0xFB, 0xE3, 0x8C);
                }

                _zOrderItems.Add(new ZOrderItem
                {
                    NoteId = noteId,
                    DisplayText = preview,
                    BgColor = bgColor,
                });
            }

            ZOrderNoteCountText.Text = $"付箋数: {_zOrderItems.Count} 枚";
        }
        finally
        {
            _isProcessingZOrderChange = false;
        }
    }

    private void OnZOrderItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isProcessingZOrderChange) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(SyncZOrderToManager));
    }

    private void SyncZOrderToManager()
    {
        if (_desktopId == Guid.Empty) return;

        var orderedIds = _zOrderItems.Select(item => item.NoteId).ToList();
        _noteManager.UpdateZOrder(_desktopId, orderedIds);
        Log.Information("設定画面: 並び順更新 ({Count}枚)", orderedIds.Count);
    }

    /// <summary>
    /// 付箋管理タブの削除ボタン（Phase 14: P14-2）
    /// </summary>
    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not Guid noteId) return;

        var item = _zOrderItems.FirstOrDefault(z => z.NoteId == noteId);
        var preview = item?.DisplayText ?? "（不明）";

        var result = MessageBox.Show(
            $"付箋「{preview}」を削除しますか？\nこの操作は元に戻せません。",
            "TopFusen — 付箋の削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _noteManager.DeleteNote(noteId);
        PopulateZOrderList();
        Log.Information("設定画面: 付箋を削除 {NoteId}", noteId);
    }

    // ==========================================
    //  詳細タブ
    // ==========================================

    private void InitializeDetailsTab()
    {
        // バージョン情報
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "不明";
        var os = Environment.OSVersion;
        VersionInfoText.Text = $"TopFusen v{version}\n"
            + $".NET {Environment.Version}\n"
            + $"OS: {os.VersionString}\n"
            + $"データ: {AppDataPaths.Base}";
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logDir = AppDataPaths.LogsDirectory;
            if (Directory.Exists(logDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = logDir,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("ログフォルダがまだ作成されていません。", "TopFusen",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ログフォルダを開けませんでした");
        }
    }

    private void CreateDiagnosticButton_Click(object sender, RoutedEventArgs e)
    {
        var saveDialog = new SaveFileDialog
        {
            Title = "診断パッケージの保存先",
            Filter = "ZIP ファイル (*.zip)|*.zip",
            FileName = $"TopFusen_Diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
            DefaultExt = ".zip"
        };

        if (saveDialog.ShowDialog() != true) return;

        try
        {
            var includeRtf = IncludeRtfCheckBox.IsChecked == true;
            CreateDiagnosticPackage(saveDialog.FileName, includeRtf);
            DiagnosticStatusText.Text = $"✓ 保存しました: {saveDialog.FileName}";
            DiagnosticStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0x33));
            Log.Information("診断パッケージを作成: {Path}", saveDialog.FileName);
        }
        catch (Exception ex)
        {
            DiagnosticStatusText.Text = $"エラー: {ex.Message}";
            DiagnosticStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x33, 0x33));
            Log.Error(ex, "診断パッケージの作成に失敗");
        }
    }

    /// <summary>
    /// 診断パッケージ（zip）を生成する（FR-DEBUG-2）
    /// 内容: logs/, settings.json, notes.json, environment.txt
    /// オプション: 付箋本文（RTF）
    /// </summary>
    private void CreateDiagnosticPackage(string zipPath, bool includeRtf)
    {
        // 先に現在の設定を保存しておく
        _persistence.FlushSave();

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // 1. ログファイル
        var logsDir = AppDataPaths.LogsDirectory;
        if (Directory.Exists(logsDir))
        {
            foreach (var logFile in Directory.GetFiles(logsDir, "*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .Take(5)) // 最新5本
            {
                try
                {
                    zip.CreateEntryFromFile(logFile, $"logs/{Path.GetFileName(logFile)}");
                }
                catch (IOException)
                {
                    // ログファイルが使用中の場合、コピーしてから追加
                    var tempCopy = Path.GetTempFileName();
                    try
                    {
                        File.Copy(logFile, tempCopy, true);
                        zip.CreateEntryFromFile(tempCopy, $"logs/{Path.GetFileName(logFile)}");
                    }
                    finally
                    {
                        File.Delete(tempCopy);
                    }
                }
            }
        }

        // 2. settings.json
        var settingsPath = AppDataPaths.SettingsJson;
        if (File.Exists(settingsPath))
        {
            zip.CreateEntryFromFile(settingsPath, "settings.json");
        }

        // 3. notes.json
        var notesPath = AppDataPaths.NotesJson;
        if (File.Exists(notesPath))
        {
            zip.CreateEntryFromFile(notesPath, "notes.json");
        }

        // 4. environment.txt
        var envEntry = zip.CreateEntry("environment.txt");
        using (var writer = new StreamWriter(envEntry.Open()))
        {
            writer.WriteLine($"TopFusen Diagnostic Package");
            writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine();
            writer.WriteLine($"App Version: {Assembly.GetExecutingAssembly().GetName().Version}");
            writer.WriteLine($".NET Version: {Environment.Version}");
            writer.WriteLine($"OS: {Environment.OSVersion.VersionString}");
            writer.WriteLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            writer.WriteLine($"64-bit Process: {Environment.Is64BitProcess}");
            writer.WriteLine($"Machine: {Environment.MachineName}");
            writer.WriteLine($"User: {Environment.UserName}");
            writer.WriteLine($"ProcessorCount: {Environment.ProcessorCount}");
            writer.WriteLine();

            // DPI
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var dpiX = source.CompositionTarget.TransformToDevice.M11;
                var dpiY = source.CompositionTarget.TransformToDevice.M22;
                writer.WriteLine($"DPI Scale: {dpiX:F2} x {dpiY:F2} ({dpiX * 100:F0}%)");
            }

            // 画面情報（WPF SystemParameters）
            writer.WriteLine();
            writer.WriteLine("Screen (Primary):");
            writer.WriteLine($"  Resolution: {SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight}");
            writer.WriteLine($"  WorkArea: {SystemParameters.WorkArea.Width}x{SystemParameters.WorkArea.Height}");
            writer.WriteLine($"  VirtualScreen: {SystemParameters.VirtualScreenWidth}x{SystemParameters.VirtualScreenHeight}");

            // 付箋情報
            writer.WriteLine();
            writer.WriteLine($"Notes: {_noteManager.Count}");
            writer.WriteLine($"IsHidden: {_noteManager.IsHidden}");
            writer.WriteLine($"IsEditMode: {_noteManager.IsEditMode}");
            writer.WriteLine($"Hotkey Enabled: {_noteManager.AppSettings.Hotkey.Enabled}");
            writer.WriteLine($"Hotkey Registered: {_hotkeyService.IsRegistered}");
            writer.WriteLine($"AutoStart: {AutoStartService.IsEnabled()}");
        }

        // 5. RTF（オプション）
        if (includeRtf)
        {
            var notesDir = AppDataPaths.NotesDirectory;
            if (Directory.Exists(notesDir))
            {
                foreach (var rtfFile in Directory.GetFiles(notesDir, "*.rtf"))
                {
                    zip.CreateEntryFromFile(rtfFile, $"notes/{Path.GetFileName(rtfFile)}");
                }
            }
        }
    }

    // ==========================================
    //  共通
    // ==========================================

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// ホットキープリセット定義
/// </summary>
internal record HotkeyPreset(string Label, int Modifiers, int Key);
