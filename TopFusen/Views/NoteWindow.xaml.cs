using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Serilog;
using TopFusen.Interop;
using TopFusen.Models;

namespace TopFusen.Views;

/// <summary>
/// 付箋ウィンドウ
/// - Borderless + TopMost + AllowsTransparency
/// - ShowInTaskbar=False + WS_EX_TOOLWINDOW で Alt+Tab から隠す
/// - クリック透過: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST フック の三重制御
/// - Phase 3: WindowChrome でリサイズ + DragMove + 選択状態管理
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

    // --- Phase 3: 選択・編集状態 ---

    /// <summary>この付箋が選択中かどうか</summary>
    public bool IsSelected { get; private set; }

    /// <summary>現在の編集モード状態</summary>
    public bool IsInEditMode { get; private set; }

    // --- Phase 3: イベント（NoteManager への通知用） ---

    /// <summary>この付箋がアクティブ化（クリック）された</summary>
    public event Action<Guid>? NoteActivated;

    /// <summary>削除がリクエストされた</summary>
    public event Action<Guid>? DeleteRequested;

    /// <summary>複製がリクエストされた</summary>
    public event Action<Guid>? DuplicateRequested;

    // --- Win32 メッセージ定数 ---
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    // --- 選択枠の視覚設定 ---
    private static readonly SolidColorBrush SelectedBorderBrush
        = new(Color.FromArgb(180, 80, 80, 80));
    private static readonly DropShadowEffect SelectedShadow
        = new() { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black };

    /// <summary>
    /// 付箋ウィンドウを生成する
    /// </summary>
    /// <param name="model">付箋データモデル</param>
    /// <param name="clickThrough">初期状態でクリック透過にするか（true=非干渉モード）</param>
    public NoteWindow(NoteModel model, bool clickThrough = true)
    {
        Model = model;
        _initialClickThrough = clickThrough;
        IsInEditMode = !clickThrough;

        InitializeComponent();

        // 初期位置・サイズの適用
        Left = model.Placement.DipX;
        Top = model.Placement.DipY;
        Width = model.Placement.DipWidth;
        Height = model.Placement.DipHeight;

        // HWND 生成後に Win32 拡張スタイルを適用
        SourceInitialized += OnSourceInitialized;
    }

    // ==========================================
    //  Win32 Interop（Phase 1〜2 から継続）
    // ==========================================

    /// <summary>
    /// HWND 生成後に拡張スタイルとメッセージフックを適用
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
        _hwndSource?.AddHook(WndProc);

        Log.Information("NoteWindow 拡張スタイル適用: {NoteId} (ClickThrough={ClickThrough}, ExStyle=0x{ExStyle:X8})",
            Model.NoteId, _isClickThrough, exStyle);
    }

    /// <summary>
    /// Win32 メッセージフック
    /// 非干渉モード時に WM_NCHITTEST で HTTRANSPARENT を返してクリック透過を実現する
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
    /// </summary>
    public void SetClickThrough(bool transparent)
    {
        if (_hwnd == IntPtr.Zero)
        {
            Log.Warning("SetClickThrough: HWND が未初期化です: {NoteId}", Model.NoteId);
            return;
        }

        if (_isClickThrough == transparent) return;

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

        if (transparent)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
            exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        }
        else
        {
            exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
            exStyle &= ~NativeMethods.WS_EX_NOACTIVATE;
        }

        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        _isClickThrough = transparent;

        Log.Information("SetClickThrough: {NoteId} → {Mode}",
            Model.NoteId, transparent ? "非干渉" : "編集");
    }

    // ==========================================
    //  Phase 3: 選択状態管理 + UI表示制御
    // ==========================================

    /// <summary>
    /// 選択状態を設定する（NoteManager から呼ばれる）
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (IsSelected == selected) return;
        IsSelected = selected;
        UpdateVisualState();
    }

    /// <summary>
    /// 編集モード状態を設定する（NoteManager から呼ばれる）
    /// </summary>
    public void SetInEditMode(bool editMode)
    {
        if (IsInEditMode == editMode) return;
        IsInEditMode = editMode;
        UpdateVisualState();
    }

    /// <summary>
    /// 選択状態・編集モードに応じてUI要素の表示を更新する
    /// - 編集ON + 選択中: ツールバー、下部アイコン、選択枠/影 を表示
    /// - 編集ON + 未選択: 本文のみ（枠なし）
    /// - 編集OFF: すべてのUI要素を非表示（本文のみ）
    /// </summary>
    private void UpdateVisualState()
    {
        var showUI = IsInEditMode && IsSelected;

        // ツールバー + 下部アイコン
        ToolbarArea.Visibility = showUI ? Visibility.Visible : Visibility.Collapsed;
        BottomBar.Visibility = showUI ? Visibility.Visible : Visibility.Collapsed;

        // 選択枠 + 影
        if (showUI)
        {
            NoteBorder.BorderBrush = SelectedBorderBrush;
            NoteBorder.Effect = SelectedShadow;
        }
        else
        {
            NoteBorder.BorderBrush = Brushes.Transparent;
            NoteBorder.Effect = null;
        }
    }

    // ==========================================
    //  Phase 3: ドラッグ移動 + ボタン操作
    // ==========================================

    /// <summary>
    /// ツールバー領域のドラッグでウィンドウを移動する（P3-2）
    /// </summary>
    private void ToolbarArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// 削除ボタンクリック（P3-5）
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(Model.NoteId);
    }

    /// <summary>
    /// 複製ボタンクリック（P3-6）
    /// </summary>
    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        DuplicateRequested?.Invoke(Model.NoteId);
    }

    /// <summary>
    /// ウィンドウがアクティブ化された時（クリックなど）
    /// 編集モード中であれば NoteManager に選択を通知する
    /// </summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (IsInEditMode)
        {
            NoteActivated?.Invoke(Model.NoteId);
        }
    }
}
