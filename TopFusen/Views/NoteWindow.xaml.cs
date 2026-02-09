using System.IO;
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
/// - クリック透過: WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WM_NCHITTEST フック の三重制御（DJ-10 で復活）
/// - VD 自前管理: DJ-10 — OS の VD 追跡に頼らず DWMWA_CLOAK で表示制御
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

    /// <summary>付箋の内容または状態が変更された（テキスト/移動/リサイズ） — Phase 5</summary>
    public event Action<Guid>? NoteChanged;

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
    /// 保留中の書式（空選択でツールバー操作時、次の入力に適用する書式）
    /// WPF の springloaded formatting がボタンクリック時に維持されない問題への対策
    /// </summary>
    private readonly Dictionary<DependencyProperty, object> _pendingFormat = new();

    // --- Phase 5: 変更追跡 ---

    /// <summary>変更追跡が有効かどうか（初期化完了後に true にする）</summary>
    private bool _isTrackingChanges;

    /// <summary>RTF コンテンツ読み込み中フラグ（TextChanged 発火抑制用）</summary>
    private bool _isLoadingContent;

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
        InitializeStyleControls(); // Phase 6: スタイル制御イベント登録
        ApplyStyle(); // Phase 6: Model.Style を UI に反映

        // 初期位置・サイズの適用
        Left = model.Placement.DipX;
        Top = model.Placement.DipY;
        Width = model.Placement.DipWidth;
        Height = model.Placement.DipHeight;

        // HWND 生成後に Win32 拡張スタイルを適用
        SourceInitialized += OnSourceInitialized;

        // Phase 5: ウィンドウ位置・サイズ変更の検知
        LocationChanged += OnWindowLocationChanged;
        SizeChanged += OnWindowSizeChanged;
    }

    // ==========================================
    //  Win32 Interop（Phase 1〜2 から継続）
    // ==========================================

    /// <summary>
    /// HWND 生成後にメッセージフックを適用
    /// DJ-7: WS_EX_TOOLWINDOW は除去（オーナーウィンドウ方式で Alt+Tab 非表示を実現）
    /// DJ-10: 生成時は WS_EX_TRANSPARENT / WS_EX_NOACTIVATE を付けない（DJ-8 維持）
    ///        Show() 後に NoteManager が SetClickThrough() で適用する
    ///        VD 自前管理のため WM_NCHITTEST フックも三重制御の一部として維持
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);

        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);

        // DJ-7: WS_EX_TOOLWINDOW は使わない（仮想デスクトップ管理から除外されるため）
        exStyle &= ~NativeMethods.WS_EX_TOOLWINDOW;

        // DJ-10: 生成時は WS_EX_TRANSPARENT / WS_EX_NOACTIVATE を付けない（DJ-8 維持）
        // これらは Show() 後に SetClickThrough() で適用される
        // 生成時にクリーンな状態にすることで、OS に通常ウィンドウとして認識させる
        exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
        exStyle &= ~NativeMethods.WS_EX_NOACTIVATE;

        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        _isClickThrough = false; // 生成時は常にクリック透過なし

        // WM_NCHITTEST メッセージフックを登録（三重制御の一部 — DJ-10 でも維持）
        _hwndSource?.AddHook(WndProc);

        Log.Information("NoteWindow 初期化: {NoteId} (ExStyle=0x{ExStyle:X8}, HasOwner={HasOwner})",
            Model.NoteId, exStyle, Owner != null);
    }

    /// <summary>
    /// Win32 メッセージフック（三重制御の一部 — DJ-10 でも維持）
    /// 非干渉モード時に WM_NCHITTEST で HTTRANSPARENT を返す
    /// WS_EX_TRANSPARENT（クロスプロセス透過）と併用して確実性を高める
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
    /// クリック透過の ON/OFF を切り替える（DJ-10: 三重制御を復活）
    /// WS_EX_TRANSPARENT + WS_EX_NOACTIVATE の Win32 スタイル操作 + WM_NCHITTEST フック
    /// ★ VD 追跡は DWMWA_CLOAK による自前管理で対処（DJ-10）
    /// </summary>
    public void SetClickThrough(bool transparent)
    {
        if (_isClickThrough == transparent) return;
        _isClickThrough = transparent;

        if (_hwnd == IntPtr.Zero) return;

        // DJ-10: Win32 スタイルを操作して WS_EX_TRANSPARENT / WS_EX_NOACTIVATE を切り替え
        // OS の VD 追跡は壊れるが、DWMWA_CLOAK で自前管理するので OK
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

        Log.Information("SetClickThrough: {NoteId} → {Mode} (ExStyle=0x{ExStyle:X8})",
            Model.NoteId, transparent ? "非干渉" : "編集", exStyle);
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

        // Phase 4/6: ポップアップを閉じる（UI非表示時）
        if (!showUI)
        {
            TextColorPopup.IsOpen = false;
            StylePopup.IsOpen = false;
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

        // RichTextBox イベント登録
        NoteRichTextBox.SelectionChanged += NoteRichTextBox_SelectionChanged;
        NoteRichTextBox.PreviewTextInput += NoteRichTextBox_PreviewTextInput;
        NoteRichTextBox.PreviewKeyDown += NoteRichTextBox_PreviewKeyDown;

        // Phase 4 P4-6: 貼り付け時のフォント正規化
        DataObject.AddPastingHandler(NoteRichTextBox, OnPasting);

        // Phase 5: テキスト変更の検知（デバウンス保存トリガー用）
        NoteRichTextBox.TextChanged += OnRichTextBoxTextChanged;
    }

    // --- 保留書式のヘルパー ---

    /// <summary>
    /// 現在有効な TextDecorations を取得する（保留書式 > 選択位置の書式）
    /// </summary>
    private TextDecorationCollection? GetEffectiveTextDecorations()
    {
        if (_pendingFormat.TryGetValue(Inline.TextDecorationsProperty, out var pd))
            return pd as TextDecorationCollection;
        return NoteRichTextBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
            as TextDecorationCollection;
    }

    /// <summary>
    /// カーソル位置の現在の書式を Run にコピーする（保留書式の Run 作成用）
    /// </summary>
    private void CopyCurrentFormattingToRun(Run run)
    {
        var sel = NoteRichTextBox.Selection;

        var weight = sel.GetPropertyValue(TextElement.FontWeightProperty);
        if (weight is FontWeight fw) run.FontWeight = fw;

        var decos = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        if (decos is TextDecorationCollection td)
            run.TextDecorations = new TextDecorationCollection(td);

        var size = sel.GetPropertyValue(TextElement.FontSizeProperty);
        if (size is double fs) run.FontSize = fs;

        var fg = sel.GetPropertyValue(TextElement.ForegroundProperty);
        if (fg is SolidColorBrush brush)
        {
            var clone = new SolidColorBrush(brush.Color);
            clone.Freeze();
            run.Foreground = clone;
        }

        // フォントは付箋単位なので Model から取得
        run.FontFamily = new FontFamily(Model.Style.FontFamilyName);
    }

    // --- 選択変更 + テキスト入力フック ---

    /// <summary>
    /// RichTextBox の選択が変更された時（カーソル移動含む）
    /// 保留書式をクリアし、ツールバー状態を更新する
    /// </summary>
    private void NoteRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInEditMode || !IsSelected) return;
        _pendingFormat.Clear();
        UpdateToolbarState();
    }

    /// <summary>
    /// テキスト入力前フック — 保留書式がある場合、書式付き Run を挿入する
    /// WPF の springloaded formatting がツールバーボタンクリック時に維持されない問題への対策。
    /// 最初の1文字を書式付き Run として挿入し、以降の入力はその Run の書式を自動引き継ぎする。
    /// </summary>
    private void NoteRichTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_pendingFormat.Count == 0) return;

        e.Handled = true;

        // カーソル位置に書式付き Run を挿入
        var pos = NoteRichTextBox.CaretPosition;
        var run = new Run(e.Text, pos);

        // カーソル位置の現在の書式を Run にコピー
        CopyCurrentFormattingToRun(run);

        // 保留書式で上書き
        foreach (var kvp in _pendingFormat)
        {
            run.SetValue(kvp.Key, kvp.Value);
        }

        // カーソルを Run の末尾に移動（以降の入力は Run の書式を自動引き継ぎ）
        NoteRichTextBox.CaretPosition = run.ContentEnd;

        // 保留書式クリア
        _pendingFormat.Clear();
    }

    /// <summary>
    /// キーダウン前フック — Ctrl+B/U をカスタム処理に統一する
    /// （WPF 標準の EditingCommands でも空選択時の書式引き継ぎが不安定なため）
    /// </summary>
    private void NoteRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        if (e.Key == Key.B)
        {
            e.Handled = true;
            BoldButton_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.U)
        {
            e.Handled = true;
            UnderlineButton_Click(this, new RoutedEventArgs());
        }
    }

    // --- ツールバー状態更新 ---

    /// <summary>
    /// ツールバーのボタン状態を現在の選択範囲（+ 保留書式）に合わせて更新する
    /// </summary>
    private void UpdateToolbarState()
    {
        _isUpdatingToolbar = true;
        try
        {
            var selection = NoteRichTextBox.Selection;

            // --- 太字 ---
            bool isBold;
            if (_pendingFormat.TryGetValue(TextElement.FontWeightProperty, out var pw))
            {
                isBold = pw is FontWeight fwp && fwp == FontWeights.Bold;
            }
            else
            {
                var fontWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
                isBold = fontWeight is FontWeight fw && fw == FontWeights.Bold;
            }
            SetToolbarButtonActive(BoldButton, isBold);

            // --- 下線・取り消し線 ---
            TextDecorationCollection? effectiveDecos;
            if (_pendingFormat.TryGetValue(Inline.TextDecorationsProperty, out var pd))
            {
                effectiveDecos = pd as TextDecorationCollection;
            }
            else
            {
                effectiveDecos = selection.GetPropertyValue(Inline.TextDecorationsProperty)
                    as TextDecorationCollection;
            }
            bool hasUnderline = effectiveDecos?.Any(d => d.Location == TextDecorationLocation.Underline) ?? false;
            bool hasStrikethrough = effectiveDecos?.Any(d => d.Location == TextDecorationLocation.Strikethrough) ?? false;
            SetToolbarButtonActive(UnderlineButton, hasUnderline);
            SetToolbarButtonActive(StrikethroughButton, hasStrikethrough);

            // --- 文字サイズ ---
            if (_pendingFormat.TryGetValue(TextElement.FontSizeProperty, out var ps))
            {
                var intSize = (int)Math.Round((double)ps);
                FontSizeCombo.SelectedItem = FontSizePresets.Contains(intSize) ? (object)intSize : null;
            }
            else
            {
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
            }

            // --- 文字色インジケータ ---
            if (_pendingFormat.TryGetValue(TextElement.ForegroundProperty, out var pf))
            {
                if (pf is SolidColorBrush b) TextColorIndicator.Fill = b;
            }
            else
            {
                var foreground = selection.GetPropertyValue(TextElement.ForegroundProperty);
                if (foreground is SolidColorBrush brush) TextColorIndicator.Fill = brush;
            }

            // --- Phase 15: テキスト配置（段落から直接取得） ---
            var para = selection.Start.Paragraph;
            var alignment = para?.TextAlignment ?? TextAlignment.Left;
            SetToolbarButtonActive(AlignLeftButton, alignment == TextAlignment.Left);
            SetToolbarButtonActive(AlignCenterButton, alignment == TextAlignment.Center);
            SetToolbarButtonActive(AlignRightButton, alignment == TextAlignment.Right);
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

    /// <summary>太字トグル（ツールバーボタン / Ctrl+B 共通）</summary>
    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        // 先にフォーカス確保（初回フォーカスの SelectionChanged で _pendingFormat がクリアされるのを防ぐ）
        NoteRichTextBox.Focus();

        var selection = NoteRichTextBox.Selection;
        if (!selection.IsEmpty)
        {
            // 選択範囲あり → 直接適用
            EditingCommands.ToggleBold.Execute(null, NoteRichTextBox);
        }
        else
        {
            // 空選択 → 保留書式でトグル
            bool isBold;
            if (_pendingFormat.TryGetValue(TextElement.FontWeightProperty, out var pw))
                isBold = pw is FontWeight fwp && fwp == FontWeights.Bold;
            else
            {
                var weight = selection.GetPropertyValue(TextElement.FontWeightProperty);
                isBold = weight is FontWeight fw && fw == FontWeights.Bold;
            }
            _pendingFormat[TextElement.FontWeightProperty] =
                isBold ? FontWeights.Normal : FontWeights.Bold;
        }
        UpdateToolbarState();
    }

    /// <summary>下線トグル（ツールバーボタン / Ctrl+U 共通）</summary>
    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        NoteRichTextBox.Focus();

        var selection = NoteRichTextBox.Selection;
        if (!selection.IsEmpty)
        {
            EditingCommands.ToggleUnderline.Execute(null, NoteRichTextBox);
        }
        else
        {
            var currentDecos = GetEffectiveTextDecorations();
            bool hasUnderline = currentDecos?.Any(d => d.Location == TextDecorationLocation.Underline) ?? false;
            var newDecos = currentDecos != null
                ? new TextDecorationCollection(currentDecos) : new TextDecorationCollection();
            if (hasUnderline)
            {
                foreach (var d in newDecos.Where(d => d.Location == TextDecorationLocation.Underline).ToList())
                    newDecos.Remove(d);
            }
            else
            {
                newDecos.Add(TextDecorations.Underline[0]);
            }
            _pendingFormat[Inline.TextDecorationsProperty] = newDecos;
        }
        UpdateToolbarState();
    }

    /// <summary>取り消し線トグル（手動実装 — EditingCommands にないため）</summary>
    private void StrikethroughButton_Click(object sender, RoutedEventArgs e)
    {
        NoteRichTextBox.Focus();

        var selection = NoteRichTextBox.Selection;
        if (!selection.IsEmpty)
        {
            // 選択範囲あり → 直接トグル
            ToggleStrikethrough();
        }
        else
        {
            // 空選択 → 保留書式でトグル
            var currentDecos = GetEffectiveTextDecorations();
            bool hasStrike = currentDecos?.Any(d => d.Location == TextDecorationLocation.Strikethrough) ?? false;
            var newDecos = currentDecos != null
                ? new TextDecorationCollection(currentDecos) : new TextDecorationCollection();
            if (hasStrike)
            {
                foreach (var d in newDecos.Where(d => d.Location == TextDecorationLocation.Strikethrough).ToList())
                    newDecos.Remove(d);
            }
            else
            {
                newDecos.Add(TextDecorations.Strikethrough[0]);
            }
            _pendingFormat[Inline.TextDecorationsProperty] = newDecos;
        }
        UpdateToolbarState();
    }

    /// <summary>
    /// 取り消し線のトグル処理（選択範囲ありの場合のみ使用）
    /// ※ 混在装飾で他の装飾が失われる可能性あり（v0.2 許容）
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
            newDecorations = new TextDecorationCollection(
                currentDecorations!.Where(d => d.Location != TextDecorationLocation.Strikethrough));
        }
        else
        {
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
            NoteRichTextBox.Focus();

            var selection = NoteRichTextBox.Selection;
            if (!selection.IsEmpty)
            {
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, (double)size);
            }
            else
            {
                _pendingFormat[TextElement.FontSizeProperty] = (double)size;
            }
            UpdateToolbarState();
        }
    }

    /// <summary>文字サイズ ComboBox のドロップダウン閉じ → RichTextBox にフォーカス戻し</summary>
    private void FontSizeCombo_DropDownClosed(object? sender, EventArgs e)
    {
        NoteRichTextBox.Focus();
    }

    // ==========================================
    //  Phase 15: テキスト配置（水平3方向 + 垂直3方向）
    // ==========================================

    /// <summary>左揃えボタンクリック</summary>
    private void AlignLeftButton_Click(object sender, RoutedEventArgs e)
    {
        NoteRichTextBox.Focus();
        EditingCommands.AlignLeft.Execute(null, NoteRichTextBox);
        UpdateToolbarState();
    }

    /// <summary>中央揃えボタンクリック</summary>
    private void AlignCenterButton_Click(object sender, RoutedEventArgs e)
    {
        NoteRichTextBox.Focus();
        EditingCommands.AlignCenter.Execute(null, NoteRichTextBox);
        UpdateToolbarState();
    }

    /// <summary>右揃えボタンクリック</summary>
    private void AlignRightButton_Click(object sender, RoutedEventArgs e)
    {
        NoteRichTextBox.Focus();
        EditingCommands.AlignRight.Execute(null, NoteRichTextBox);
        UpdateToolbarState();
    }

    /// <summary>
    /// 垂直配置サイクルボタンクリック（上揃え → 中央揃え → 下揃え → 上揃え…）
    /// 付箋単位の設定で、NoteStyle に保存される
    /// </summary>
    private void VerticalAlignButton_Click(object sender, RoutedEventArgs e)
    {
        var current = Model.Style.VerticalTextAlignment;
        var next = current switch
        {
            "top" => "center",
            "center" => "bottom",
            _ => "top",
        };
        Model.Style.VerticalTextAlignment = next;
        ApplyVerticalAlignment();
        UpdateVerticalAlignButton();
        NotifyStyleChanged();
    }

    /// <summary>
    /// 垂直配置を RichTextBox に適用する
    /// </summary>
    private void ApplyVerticalAlignment()
    {
        NoteRichTextBox.VerticalContentAlignment = Model.Style.VerticalTextAlignment switch
        {
            "center" => VerticalAlignment.Center,
            "bottom" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Top,
        };
    }

    /// <summary>
    /// 垂直配置ボタンのアイコンとツールチップを現在の状態に合わせて更新する
    /// </summary>
    private void UpdateVerticalAlignButton()
    {
        var align = Model.Style.VerticalTextAlignment;
        VerticalAlignIcon.Text = align switch
        {
            "center" => "⬍",
            "bottom" => "⬇",
            _ => "⬆",
        };
        VerticalAlignButton.ToolTip = align switch
        {
            "top" => "垂直: 上揃え（クリックで中央揃えに変更）",
            "center" => "垂直: 中央揃え（クリックで下揃えに変更）",
            "bottom" => "垂直: 下揃え（クリックで上揃えに変更）",
            _ => "垂直配置",
        };
        SetToolbarButtonActive(VerticalAlignButton, align != "top");
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

            NoteRichTextBox.Focus();

            var selection = NoteRichTextBox.Selection;
            if (!selection.IsEmpty)
            {
                selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            }
            else
            {
                _pendingFormat[TextElement.ForegroundProperty] = brush;
            }

            TextColorIndicator.Fill = brush;
            TextColorPopup.IsOpen = false;
            UpdateToolbarState();
        }
    }

    // ==========================================
    //  Phase 6: スタイル制御（FR-STYLE / FR-FONT）
    // ==========================================

    /// <summary>フォント許可リスト（NoteManager から設定される）</summary>
    private IReadOnlyList<string> _fontAllowList = new List<string> { "Yu Gothic UI" };

    /// <summary>スタイルUI更新中フラグ（フィードバックループ防止）</summary>
    private bool _isUpdatingStyle;

    /// <summary>
    /// フォント許可リストを設定する（NoteManager から呼ばれる）
    /// </summary>
    public void SetFontAllowList(IReadOnlyList<string> fonts)
    {
        _fontAllowList = fonts;
    }

    /// <summary>
    /// スタイル関連のイベントを登録する（コンストラクタから呼ばれる）
    /// ※ XAML ではなくコードで登録し、InitializeComponent 中の不要なイベント発火を防ぐ
    /// </summary>
    private void InitializeStyleControls()
    {
        OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;
        FontFamilyCombo.SelectionChanged += FontFamilyCombo_SelectionChanged;
        VividCategoryRadio.Checked += BgPaletteCategory_Changed;
        NaturalCategoryRadio.Checked += BgPaletteCategory_Changed;
    }

    /// <summary>
    /// Model.Style の値を UI に反映する（背景色 + 不透明度 + フォント）
    /// 作成時・復元時・スタイル変更時に呼ばれる
    /// </summary>
    public void ApplyStyle()
    {
        // 1. 背景色 + 不透明度（背景の Alpha チャネルで制御 — テキスト・枠は不透明のまま）
        var hexColor = PaletteDefinitions.GetHexColor(
            Model.Style.BgPaletteCategoryId, Model.Style.BgColorId)
            ?? PaletteDefinitions.DefaultHexColor;

        try
        {
            var baseColor = (Color)ColorConverter.ConvertFromString(hexColor);
            var alpha = (byte)Math.Clamp(Model.Style.Opacity0to100 * 255 / 100, 0, 255);
            var bgColor = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
            var bgBrush = new SolidColorBrush(bgColor);
            bgBrush.Freeze();
            NoteBorder.Background = bgBrush;
        }
        catch
        {
            // フォールバック: デフォルト黄色（不透明）
            NoteBorder.Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xE3, 0x8C));
        }

        // 2. スタイルカラーインジケータ更新（下部バーの丸いアイコン）
        try
        {
            var indicatorColor = (Color)ColorConverter.ConvertFromString(hexColor);
            var indicatorBrush = new SolidColorBrush(indicatorColor);
            indicatorBrush.Freeze();
            StyleColorIndicator.Fill = indicatorBrush;
        }
        catch { /* ignore */ }

        // 3. フォント（付箋単位）
        var fontFamily = new FontFamily(Model.Style.FontFamilyName);
        NoteRichTextBox.FontFamily = fontFamily;
        if (NoteRichTextBox.Document != null)
        {
            NoteRichTextBox.Document.FontFamily = fontFamily;
        }

        // 4. Phase 15: 垂直配置（付箋単位）
        ApplyVerticalAlignment();
        UpdateVerticalAlignButton();

        Log.Debug("スタイル適用: {NoteId} (Bg={Cat}/{Color}, Opacity={Opacity}, Font={Font}, VAlign={VAlign})",
            Model.NoteId, Model.Style.BgPaletteCategoryId, Model.Style.BgColorId,
            Model.Style.Opacity0to100, Model.Style.FontFamilyName, Model.Style.VerticalTextAlignment);
    }

    // --- スタイル Popup ---

    /// <summary>スタイル設定ボタンクリック → Popup 開閉</summary>
    private void StyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (StylePopup.IsOpen)
        {
            StylePopup.IsOpen = false;
            return;
        }

        SyncStylePopup();
        StylePopup.IsOpen = true;
    }

    /// <summary>スタイル Popup の UI を現在の Model.Style に同期する</summary>
    private void SyncStylePopup()
    {
        _isUpdatingStyle = true;
        try
        {
            // カテゴリラジオ
            VividCategoryRadio.IsChecked = Model.Style.BgPaletteCategoryId == "vivid";
            NaturalCategoryRadio.IsChecked = Model.Style.BgPaletteCategoryId != "vivid";

            // 色グリッド
            PopulateBgColorGrid(Model.Style.BgPaletteCategoryId);

            // 不透明度スライダー
            OpacitySlider.Value = Model.Style.Opacity0to100;
            OpacityValueText.Text = $"{Model.Style.Opacity0to100}%";

            // フォント ComboBox
            FontFamilyCombo.Items.Clear();
            foreach (var font in _fontAllowList)
            {
                FontFamilyCombo.Items.Add(font);
            }
            FontFamilyCombo.SelectedItem = Model.Style.FontFamilyName;

            // 現在のフォントが許可リストにない場合は一時的に追加
            if (FontFamilyCombo.SelectedItem == null && !string.IsNullOrEmpty(Model.Style.FontFamilyName))
            {
                FontFamilyCombo.Items.Insert(0, Model.Style.FontFamilyName);
                FontFamilyCombo.SelectedItem = Model.Style.FontFamilyName;
            }
        }
        finally
        {
            _isUpdatingStyle = false;
        }
    }

    /// <summary>背景色グリッドを指定カテゴリの色で再構築する</summary>
    private void PopulateBgColorGrid(string categoryId)
    {
        BgColorGrid.Children.Clear();
        var category = PaletteDefinitions.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null) return;

        foreach (var paletteColor in category.Colors)
        {
            var isSelected = Model.Style.BgPaletteCategoryId == categoryId
                             && Model.Style.BgColorId == paletteColor.Id;

            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Focusable = false,
                Tag = $"{categoryId}:{paletteColor.Id}",
                ToolTip = paletteColor.DisplayName,
            };

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(paletteColor.HexColor);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                btn.Background = brush;
            }
            catch
            {
                btn.Background = Brushes.Gray;
            }

            if (isSelected)
            {
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                btn.BorderThickness = new Thickness(2.5);
            }
            else
            {
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
                btn.BorderThickness = new Thickness(1);
            }

            btn.Click += BgColorSwatch_Click;
            BgColorGrid.Children.Add(btn);
        }
    }

    /// <summary>背景色カテゴリ変更（ラジオボタン）</summary>
    private void BgPaletteCategory_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingStyle) return;
        var categoryId = VividCategoryRadio.IsChecked == true ? "vivid" : "natural";
        PopulateBgColorGrid(categoryId);
    }

    /// <summary>背景色スウォッチクリック → 背景色を変更して即時反映</summary>
    private void BgColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        var parts = tag.Split(':');
        if (parts.Length != 2) return;

        Model.Style.BgPaletteCategoryId = parts[0];
        Model.Style.BgColorId = parts[1];

        ApplyStyle();
        PopulateBgColorGrid(parts[0]); // 選択ハイライト更新
        NotifyStyleChanged();
    }

    /// <summary>不透明度スライダー変更 → 即時反映</summary>
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingStyle) return;
        if (OpacityValueText == null) return; // InitializeComponent 完了前

        var opacity = (int)OpacitySlider.Value;
        Model.Style.Opacity0to100 = opacity;
        OpacityValueText.Text = $"{opacity}%";
        ApplyStyle();
        NotifyStyleChanged();
    }

    /// <summary>フォント ComboBox 変更 → 付箋全体のフォントを変更</summary>
    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingStyle) return;
        if (FontFamilyCombo.SelectedItem is not string fontName) return;

        Model.Style.FontFamilyName = fontName;
        ApplyStyle();

        // ドキュメント全体にフォントを適用（付箋単位フォント — FR-FONT）
        ApplyFontToDocument(fontName);

        NotifyStyleChanged();
    }

    /// <summary>ドキュメント全体にフォントを適用する（付箋単位フォント変更時）</summary>
    private void ApplyFontToDocument(string fontFamilyName)
    {
        var doc = NoteRichTextBox.Document;
        if (doc == null) return;

        var noteFont = new FontFamily(fontFamilyName);
        var fullRange = new TextRange(doc.ContentStart, doc.ContentEnd);
        fullRange.ApplyPropertyValue(TextElement.FontFamilyProperty, noteFont);

        Log.Debug("フォント変更（全体適用）: {NoteId} → {Font}", Model.NoteId, fontFamilyName);
    }

    /// <summary>スタイル変更を通知する（デバウンス保存トリガー）</summary>
    private void NotifyStyleChanged()
    {
        if (!_isTrackingChanges) return;
        NoteChanged?.Invoke(Model.NoteId);
    }

    // --- P4-6: クリップボード + フォント正規化 ---

    /// <summary>
    /// 貼り付け時のフォント正規化（FR-TEXT-6 / FR-FONT）
    /// </summary>
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(NormalizePastedFont));
    }

    /// <summary>
    /// 貼り付け後にドキュメント全体のフォントファミリーを付箋フォントに正規化する
    /// </summary>
    private void NormalizePastedFont()
    {
        var doc = NoteRichTextBox.Document;
        if (doc == null) return;

        var noteFont = new FontFamily(Model.Style.FontFamilyName);
        var fullRange = new TextRange(doc.ContentStart, doc.ContentEnd);
        fullRange.ApplyPropertyValue(TextElement.FontFamilyProperty, noteFont);

        Log.Debug("貼り付け後のフォント正規化: {NoteId} → {Font}",
            Model.NoteId, Model.Style.FontFamilyName);
    }

    // ==========================================
    //  Phase 5: 永続化連携
    // ==========================================

    /// <summary>
    /// 変更追跡を有効にする（NoteManager が初期化完了後に呼ぶ）
    /// 初期化中の LocationChanged / SizeChanged / TextChanged で不要な保存が走るのを防ぐ
    /// </summary>
    public void EnableChangeTracking()
    {
        _isTrackingChanges = true;
    }

    /// <summary>
    /// RichTextBox の内容を RTF バイト配列として取得する
    /// </summary>
    public byte[] GetRtfBytes()
    {
        try
        {
            var range = new TextRange(
                NoteRichTextBox.Document.ContentStart,
                NoteRichTextBox.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.Rtf);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RTF バイト取得失敗: {NoteId}", Model.NoteId);
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// RTF バイト配列から RichTextBox の内容を復元する
    /// TextChanged イベントは抑制される（_isLoadingContent フラグ）
    /// </summary>
    public void LoadRtfBytes(byte[] rtfContent)
    {
        if (rtfContent.Length == 0) return;

        _isLoadingContent = true;
        try
        {
            var range = new TextRange(
                NoteRichTextBox.Document.ContentStart,
                NoteRichTextBox.Document.ContentEnd);
            using var ms = new MemoryStream(rtfContent);
            range.Load(ms, DataFormats.Rtf);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RTF バイト読み込み失敗: {NoteId}", Model.NoteId);
        }
        finally
        {
            _isLoadingContent = false;
        }
    }

    /// <summary>
    /// ウィンドウの現在の位置・サイズを Model に同期する
    /// </summary>
    public void SyncModelFromWindow()
    {
        Model.Placement.DipX = Left;
        Model.Placement.DipY = Top;
        Model.Placement.DipWidth = ActualWidth > 0 ? ActualWidth : Width;
        Model.Placement.DipHeight = ActualHeight > 0 ? ActualHeight : Height;
    }

    /// <summary>
    /// RichTextBox の先頭行テキストを取得する（Z順一覧表示用）
    /// </summary>
    public string GetFirstLinePreview()
    {
        var doc = NoteRichTextBox.Document;
        if (doc == null) return string.Empty;

        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        var text = range.Text.TrimStart();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var firstLine = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(firstLine)) return string.Empty;
        return firstLine.Length > 50 ? firstLine[..50] + "…" : firstLine;
    }

    // --- 変更検知ハンドラ ---

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (!_isTrackingChanges) return;
        SyncModelFromWindow();
        NoteChanged?.Invoke(Model.NoteId);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isTrackingChanges) return;
        SyncModelFromWindow();
        NoteChanged?.Invoke(Model.NoteId);
    }

    private void OnRichTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isTrackingChanges || _isLoadingContent) return;
        Model.FirstLinePreview = GetFirstLinePreview();
        NoteChanged?.Invoke(Model.NoteId);
    }
}
