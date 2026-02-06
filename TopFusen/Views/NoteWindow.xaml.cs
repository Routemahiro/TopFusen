using System.Windows;
using System.Windows.Interop;
using Serilog;
using TopFusen.Interop;
using TopFusen.Models;

namespace TopFusen.Views;

/// <summary>
/// 付箋ウィンドウ
/// - Borderless + TopMost + AllowsTransparency
/// - ShowInTaskbar=False + WS_EX_TOOLWINDOW で Alt+Tab から隠す
/// - Phase 2 でクリック透過（WS_EX_TRANSPARENT）を追加予定
/// </summary>
public partial class NoteWindow : Window
{
    /// <summary>この付箋に対応するデータモデル</summary>
    public NoteModel Model { get; }

    public NoteWindow(NoteModel model)
    {
        Model = model;
        InitializeComponent();

        // 初期位置・サイズの適用
        Left = model.Placement.DipX;
        Top = model.Placement.DipY;
        Width = model.Placement.DipWidth;
        Height = model.Placement.DipHeight;

        // HWND 生成後に Win32 拡張スタイルを適用
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// HWND 生成後に WS_EX_TOOLWINDOW を付与して Alt+Tab / タスクバーから隠す
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        // Alt+Tab / タスクバーに表示しない
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle &= ~NativeMethods.WS_EX_APPWINDOW;

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        Log.Debug("NoteWindow WS_EX_TOOLWINDOW 適用完了: {NoteId}", Model.NoteId);
    }
}
