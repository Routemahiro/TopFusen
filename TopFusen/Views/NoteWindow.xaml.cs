using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Serilog;
using TopFusen.Interop;
using TopFusen.Models;

namespace TopFusen.Views;

/// <summary>
/// 付箋ウィンドウ
/// - Borderless + TopMost + AllowsTransparency
/// - Alt+Tab 非表示: オーナーウィンドウ方式（DJ-7）+ ShowInTaskbar=False
///   ※ WS_EX_TOOLWINDOW は仮想デスクトップ管理から除外されるため使用しない
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

    // --- Phase 4: ツールバー ---
    /// <summary>ツールバーの文字サイズプリセット</summary>
    private static readonly int[] FontSizePresets = { 8, 10, 12, 14, 16, 18, 20, 24, 28, 36, 48 };
    /// <summary>ツールバーボタンのアクティブ時背景色</summary>
    private static readonly SolidColorBrush ToolbarActiveBg = new(Color.FromArgb(60, 0, 0, 0));
    /// <summary>ツールバー状態更新中フラグ（フィードバックループ防止）</summary>
    private bool _isUpdatingToolbar;

    /// <summary>
    /// 付箋ウィンドウを生成する
    /// </summary>
    /// <param name="model">付箋データモデル</param>
    /// <param name="clickThrough">初期状態でクリック透過にするか（true=非干渉モード）</param>
    public NoteWindow(NoteModel model, bool clickThrough = true)
    {
        Model = model;
        _initialClickThrough = clickThrough;
        // DJ-8: 常に編集OFF（UI非表示）で起動する安全な初期状態
        // 実際の編集モードは NoteManager が Show() 後に SetInEditMode() で設定する
        IsInEditMode = false;

        InitializeComponent();
        InitializeToolbar(); // Phase 4: ツールバー初期化

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
    /// DJ-7: WS_EX_TOOLWINDOW は除去（オーナーウィンドウ方式で Alt+Tab 非表示を実現）
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

        // DJ-7: WS_EX_TOOLWINDOW は使わない（仮想デスクトップ管理から除外されるため）
        // Alt+Tab 非表示は Owner ウィンドウ + ShowInTaskbar=false で実現
        // 念のため TOOLWINDOW が付いていたら外す
        exStyle &= ~NativeMethods.WS_EX_TOOLWINDOW;

        // 初期クリック透過状態の適用（三重制御の一部）
        if (_initialClickThrough)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
            exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        }

        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        _isClickThrough = _initialClickThrough;

        // WM_NCHITTEST メッセージフックを登録（三重制御の一部）
        _hwndSource?.AddHook(WndProc);

        Log.Information("NoteWindow 拡張スタイル適用: {NoteId} (ClickThrough={ClickThrough}, ExStyle=0x{ExStyle:X8}, HasOwner={HasOwner})",
            Model.NoteId, _isClickThrough, exStyle, Owner != null);
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
    /// - 編集ON + 選択中: ツールバー、下部アイコン、選択枠/影、テキスト編集可能
    /// - 編集ON + 未選択: 本文のみ（枠なし、クリックで選択可能）
    /// - 編集OFF: すべてのUI要素を非表示（本文のみ、クリック透過）
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

        // Phase 4: RichTextBox の編集状態管理
        var canEdit = IsInEditMode && IsSelected;
        NoteRichTextBox.IsReadOnly = !canEdit;
        NoteRichTextBox.Focusable = canEdit;
        // 編集モード中はクリックを受け付ける（未選択でもクリック→Window Activated→選択）
        // 非干渉モードではクリック透過（WS_EX_TRANSPARENT が Window レベルで処理）
        NoteRichTextBox.IsHitTestVisible = IsInEditMode;
        NoteRichTextBox.VerticalScrollBarVisibility =
            canEdit ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;

        // フォーカスクリア（編集不可になった場合）
        if (!canEdit && NoteRichTextBox.IsKeyboardFocused)
        {
            Keyboard.ClearFocus();
        }

        // Phase 4: ポップアップを閉じる（UI非表示時）
        if (!showUI)
        {
            TextColorPopup.IsOpen = false;
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

    // ==========================================
    //  Phase 4: リッチテキスト装飾ツールバー
    // ==========================================

    /// <summary>
    /// ツールバーの初期化（ComboBox のプリセット値設定 + イベント登録）
    /// </summary>
    private void InitializeToolbar()
    {
        // 文字サイズプリセットを ComboBox に追加
        foreach (var size in FontSizePresets)
        {
            FontSizeCombo.Items.Add(size);
        }
        FontSizeCombo.SelectedItem = 14;

        // RichTextBox の選択変更でツールバー状態を更新
        NoteRichTextBox.SelectionChanged += NoteRichTextBox_SelectionChanged;
    }

    /// <summary>
    /// RichTextBox の選択が変更された時にツールバーの状態を更新する
    /// </summary>
    private void NoteRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInEditMode || !IsSelected) return;
        UpdateToolbarState();
    }

    /// <summary>
    /// ツールバーのボタン状態を現在の選択範囲に合わせて更新する
    /// </summary>
    private void UpdateToolbarState()
    {
        _isUpdatingToolbar = true;
        try
        {
            var selection = NoteRichTextBox.Selection;

            // --- 太字 ---
            var fontWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
            SetToolbarButtonActive(BoldButton,
                fontWeight is FontWeight fw && fw == FontWeights.Bold);

            // --- 下線・取り消し線 ---
            var textDeco = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            bool hasUnderline = false;
            bool hasStrikethrough = false;
            if (textDeco is TextDecorationCollection decos)
            {
                hasUnderline = decos.Any(d => d.Location == TextDecorationLocation.Underline);
                hasStrikethrough = decos.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            }
            SetToolbarButtonActive(UnderlineButton, hasUnderline);
            SetToolbarButtonActive(StrikethroughButton, hasStrikethrough);

            // --- 文字サイズ ---
            var fontSize = selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (fontSize is double size)
            {
                var intSize = (int)Math.Round(size);
                FontSizeCombo.SelectedItem = FontSizePresets.Contains(intSize) ? (object)intSize : null;
            }
            else
            {
                FontSizeCombo.SelectedItem = null;
            }

            // --- 文字色インジケータ ---
            var foreground = selection.GetPropertyValue(TextElement.ForegroundProperty);
            if (foreground is SolidColorBrush brush)
            {
                TextColorIndicator.Fill = brush;
            }
        }
        finally
        {
            _isUpdatingToolbar = false;
        }
    }

    /// <summary>
    /// ツールバーボタンのアクティブ/非アクティブ表示を切り替える
    /// </summary>
    private static void SetToolbarButtonActive(Button button, bool isActive)
    {
        button.Background = isActive ? ToolbarActiveBg : Brushes.Transparent;
    }

    // --- 装飾ボタン Click ハンドラ ---

    /// <summary>太字トグル（Ctrl+B と同等）</summary>
    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBold.Execute(null, NoteRichTextBox);
        NoteRichTextBox.Focus();
    }

    /// <summary>下線トグル（Ctrl+U と同等）</summary>
    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleUnderline.Execute(null, NoteRichTextBox);
        NoteRichTextBox.Focus();
    }

    /// <summary>取り消し線トグル（手動実装 — EditingCommands にないため）</summary>
    private void StrikethroughButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleStrikethrough();
        NoteRichTextBox.Focus();
    }

    /// <summary>
    /// 取り消し線のトグル処理
    /// 選択範囲の TextDecorations から Strikethrough を追加/除去する
    /// ※ 選択範囲が混在装飾の場合、既存の他の装飾が失われる可能性あり（v0.2 許容）
    /// </summary>
    private void ToggleStrikethrough()
    {
        var selection = NoteRichTextBox.Selection;
        var currentDecorations = selection.GetPropertyValue(Inline.TextDecorationsProperty)
            as TextDecorationCollection;

        bool hasStrikethrough = currentDecorations != null &&
            currentDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);

        TextDecorationCollection newDecorations;
        if (hasStrikethrough)
        {
            // 取り消し線を除去（他の装飾は保持）
            newDecorations = new TextDecorationCollection(
                currentDecorations!.Where(d => d.Location != TextDecorationLocation.Strikethrough));
        }
        else
        {
            // 取り消し線を追加（他の装飾は保持）
            newDecorations = currentDecorations != null
                ? new TextDecorationCollection(currentDecorations)
                : new TextDecorationCollection();
            newDecorations.Add(TextDecorations.Strikethrough[0]);
        }

        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecorations);
    }

    // --- 文字サイズ ComboBox ---

    /// <summary>文字サイズ ComboBox の選択変更</summary>
    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingToolbar) return;
        if (FontSizeCombo.SelectedItem is int size)
        {
            NoteRichTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, (double)size);
            NoteRichTextBox.Focus();
        }
    }

    /// <summary>文字サイズ ComboBox のドロップダウン閉じ → RichTextBox にフォーカス戻し</summary>
    private void FontSizeCombo_DropDownClosed(object? sender, EventArgs e)
    {
        NoteRichTextBox.Focus();
    }

    // --- 文字色パレット ---

    /// <summary>文字色ボタン → パレット Popup を開閉</summary>
    private void TextColorButton_Click(object sender, RoutedEventArgs e)
    {
        TextColorPopup.IsOpen = !TextColorPopup.IsOpen;
    }

    /// <summary>文字色パレットのスウォッチクリック → 選択範囲に色を適用</summary>
    private void TextColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorStr)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorStr);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            NoteRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            TextColorIndicator.Fill = brush;
            TextColorPopup.IsOpen = false;
            NoteRichTextBox.Focus();
        }
    }
}
