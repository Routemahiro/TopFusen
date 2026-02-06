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
/// - クリック透過: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST フック の三重制御
///   WPF AllowsTransparency=True（WS_EX_LAYERED）環境では WS_EX_TRANSPARENT 単独では
///   クリック透過の ON/OFF が効かないケースがあるため、WM_NCHITTEST で HTTRANSPARENT を返す方式を併用
/// </summary>
public partial class NoteWindow : Window
{
    /// <summary>この付箋に対応するデータモデル</summary>
    public NoteModel Model { get; }

    /// <summary>HWND ハンドル（SourceInitialized 後に有効）</summary>
    private IntPtr _hwnd;

    /// <summary>WPF HwndSource（メッセージフック登録用）</summary>
    private HwndSource? _hwndSource;

    /// <summary>現在クリック透過中かどうか</summary>
    private bool _isClickThrough;

    /// <summary>初期のクリック透過状態（コンストラクタで決定、OnSourceInitialized で適用）</summary>
    private readonly bool _initialClickThrough;

    // Win32 メッセージ定数
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    /// <summary>
    /// 付箋ウィンドウを生成する
    /// </summary>
    /// <param name="model">付箋データモデル</param>
    /// <param name="clickThrough">初期状態でクリック透過にするか（true=非干渉モード）</param>
    public NoteWindow(NoteModel model, bool clickThrough = true)
    {
        Model = model;
        _initialClickThrough = clickThrough;

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
    /// HWND 生成後に拡張スタイルとメッセージフックを適用
    /// - WS_EX_TOOLWINDOW: Alt+Tab / タスクバーから隠す
    /// - WS_EX_TRANSPARENT + WS_EX_NOACTIVATE: クリック透過（Win32 レベル）
    /// - WM_NCHITTEST フック: クリック透過（WPF メッセージレベル）
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

        // Alt+Tab / タスクバーに表示しない
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle &= ~NativeMethods.WS_EX_APPWINDOW;

        // 初期クリック透過状態の適用
        if (_initialClickThrough)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
            exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        }

        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        _isClickThrough = _initialClickThrough;

        // WM_NCHITTEST メッセージフックを登録
        // AllowsTransparency=True + WS_EX_LAYERED 環境での確実なクリック透過制御
        _hwndSource?.AddHook(WndProc);

        Log.Information("NoteWindow 拡張スタイル適用: {NoteId} (ClickThrough={ClickThrough}, ExStyle=0x{ExStyle:X8})",
            Model.NoteId, _isClickThrough, exStyle);
    }

    /// <summary>
    /// Win32 メッセージフック
    /// 非干渉モード時に WM_NCHITTEST で HTTRANSPARENT を返してクリック透過を実現する
    /// WS_EX_TRANSPARENT だけでは WPF AllowsTransparency との共存で不十分なため、
    /// メッセージレベルでの制御を併用する
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && _isClickThrough)
        {
            handled = true;
            return new IntPtr(HTTRANSPARENT);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// クリック透過の ON/OFF を切り替える（三重制御）
    /// 1. WS_EX_TRANSPARENT: Win32 レベルのクリック透過
    /// 2. WS_EX_NOACTIVATE: フォーカスを奪わない
    /// 3. WM_NCHITTEST フック: _isClickThrough フラグで HTTRANSPARENT を返す
    /// </summary>
    /// <param name="transparent">true: クリック透過（非干渉モード）, false: クリック可能（編集モード）</param>
    public void SetClickThrough(bool transparent)
    {
        if (_hwnd == IntPtr.Zero)
        {
            Log.Warning("SetClickThrough: HWND が未初期化です: {NoteId}", Model.NoteId);
            return;
        }

        if (_isClickThrough == transparent) return; // 変更なし

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

        Log.Information("SetClickThrough 変更前: {NoteId} ExStyle=0x{ExStyle:X8}", Model.NoteId, exStyle);

        if (transparent)
        {
            // 非干渉モード: クリック透過 + フォーカスを奪わない
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
            exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        }
        else
        {
            // 編集モード: クリック透過解除
            exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
            exStyle &= ~NativeMethods.WS_EX_NOACTIVATE;
        }

        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        // _isClickThrough を更新 → WM_NCHITTEST フックの判定に即時反映
        _isClickThrough = transparent;

        // 変更が実際に適用されたか確認
        var newExStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        Log.Information("SetClickThrough 変更後: {NoteId} → {Mode} ExStyle=0x{ExStyle:X8}",
            Model.NoteId, transparent ? "非干渉（透過）" : "編集（操作可能）", newExStyle);
    }
}
