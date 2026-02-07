using System.Windows;
using System.Windows.Interop;
using Serilog;
using TopFusen.Models;
using TopFusen.Interop;
using TopFusen.Views;

namespace TopFusen.Services;

/// <summary>
/// 付箋のライフサイクル管理（生成 / 保持 / 破棄 / モード切替 / 選択管理 / 永続化連携 / VD 表示制御）
/// Phase 1: メモリ上のみ管理
/// Phase 2: 編集モード管理 + クリック透過の一括制御
/// Phase 3: 選択状態管理 + 複製 + イベント連携
/// Phase 3.7: DJ-7 対応 — オーナーウィンドウ方式で Alt+Tab 非表示 + 仮想デスクトップ参加
/// Phase 5: 永続化連携（PersistenceService との統合 — SaveAll / LoadAll / 変更追跡）
/// Phase 8: DJ-10 — VD 自前管理（DWMWA_CLOAK + DesktopId 付与 + 切替時 Cloak/Uncloak + 喪失フォールバック）
/// </summary>
public class NoteManager
{
    private readonly List<(NoteModel Model, NoteWindow Window)> _notes = new();
    private readonly PersistenceService _persistence;
    private readonly VirtualDesktopService _vdService;

    /// <summary>アプリケーション設定（永続化される）</summary>
    private AppSettings _appSettings = new();

    /// <summary>
    /// 全 NoteWindow のオーナーとなる非表示ウィンドウ（DJ-7 対応）
    /// オーナー付きウィンドウは Alt+Tab に表示されないため、WS_EX_TOOLWINDOW が不要になる。
    /// これにより仮想デスクトップの MoveWindowToDesktop が正常に機能する。
    /// </summary>
    private Window? _ownerWindow;

    /// <summary>管理中の全付箋モデル</summary>
    public IReadOnlyList<NoteModel> Notes
        => _notes.Select(n => n.Model).ToList().AsReadOnly();

    /// <summary>管理中の全付箋ウィンドウ</summary>
    public IReadOnlyList<NoteWindow> Windows
        => _notes.Select(n => n.Window).ToList().AsReadOnly();

    /// <summary>管理中の付箋数</summary>
    public int Count => _notes.Count;

    /// <summary>現在の編集モード状態（false=非干渉、true=編集可能）</summary>
    public bool IsEditMode { get; private set; }

    /// <summary>付箋が一時非表示中かどうか（永続化される）</summary>
    public bool IsHidden => _appSettings.IsHidden;

    /// <summary>現在選択中の付箋ID（null=選択なし）</summary>
    public Guid? SelectedNoteId { get; private set; }

    /// <summary>アプリケーション設定への参照（読み取り用）</summary>
    public AppSettings AppSettings => _appSettings;

    /// <summary>オーナーウィンドウの HWND（VD Tracker 等の外部利用向け）</summary>
    public IntPtr OwnerHandle => _ownerWindow != null
        ? new WindowInteropHelper(_ownerWindow).Handle
        : IntPtr.Zero;

    // ==========================================
    //  コンストラクタ（Phase 5: DI + Phase 8: VD サービス）
    // ==========================================

    public NoteManager(PersistenceService persistence, VirtualDesktopService vdService)
    {
        _persistence = persistence;
        _vdService = vdService;
        _persistence.SaveRequested += SaveAll;
    }

    // ==========================================
    //  Phase 3.7: オーナーウィンドウ管理（DJ-7 対応）
    // ==========================================

    /// <summary>
    /// オーナーウィンドウを初期化する（アプリ起動時に1回呼ぶ）
    /// このウィンドウは非表示で、全 NoteWindow の Owner として機能する。
    /// Owner 付きウィンドウは Alt+Tab に表示されず、かつ仮想デスクトップ管理に正常参加する。
    /// </summary>
    public void InitializeOwnerWindow()
    {
        if (_ownerWindow != null) return;

        _ownerWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Visibility = Visibility.Hidden,
            Title = "TopFusen_OwnerWindow"
        };

        // HWND を生成する（Show せずにハンドルを確保）
        var helper = new WindowInteropHelper(_ownerWindow);
        helper.EnsureHandle();

        Log.Information("オーナーウィンドウを初期化しました (HWND=0x{Handle:X8})", helper.Handle);
    }

    // ==========================================
    //  編集モード管理
    // ==========================================

    /// <summary>
    /// 編集モードを切り替え、全付箋ウィンドウに一括反映する
    /// DJ-10: 
    ///   編集 OFF: ① 非現在VD付箋を Cloak → ② 全付箋に WS_EX_TRANSPARENT 付与
    ///   編集 ON:  ① 現在VD付箋のみ WS_EX_TRANSPARENT 除去（非現在VDは WS_EX_TRANSPARENT 維持で OS 干渉回避）
    ///            ② 現在VD付箋を Uncloak
    /// ★ 非現在VD付箋は常に WS_EX_TRANSPARENT を維持する（OS の VD 追跡が介入しないようにする）
    /// </summary>
    public void SetEditMode(bool isEditMode)
    {
        IsEditMode = isEditMode;
        var currentDesktop = _vdService.IsAvailable ? _vdService.GetCurrentDesktopIdFast() : null;

        if (!isEditMode)
        {
            // === 編集 OFF 遷移 ===
            // ① 非現在VD付箋を先に Cloak（ちらつき対策）
            if (currentDesktop.HasValue)
            {
                foreach (var (model, window) in _notes)
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero) continue;

                    if (model.DesktopId != Guid.Empty && model.DesktopId != currentDesktop.Value)
                    {
                        VirtualDesktopService.CloakWindow(hwnd);
                    }
                }
            }

            // ② 全付箋に WS_EX_TRANSPARENT 付与 + 編集 OFF
            foreach (var (_, window) in _notes)
            {
                window.SetClickThrough(true);
                window.SetInEditMode(false);
            }

            DeselectAll();
        }
        else
        {
            // === 編集 ON 遷移 ===
            foreach (var (model, window) in _notes)
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var belongsHere = model.DesktopId == Guid.Empty
                    || !currentDesktop.HasValue
                    || model.DesktopId == currentDesktop.Value;

                if (belongsHere)
                {
                    // 現在VD付箋: WS_EX_TRANSPARENT 除去（クリック可能に）+ 編集 ON + Uncloak
                    window.SetClickThrough(false);
                    window.SetInEditMode(true);
                    if (hwnd != IntPtr.Zero)
                    {
                        VirtualDesktopService.UncloakWindow(hwnd);
                    }
                }
                else
                {
                    // 非現在VD付箋: WS_EX_TRANSPARENT 維持（OS VD 追跡を回避）+ Cloak 維持
                    // 編集モードは設定しない（見えないので不要）
                }
            }
        }

        // Phase 9: 編集ON時に Z順を再適用（Uncloak 後に順序を確定させる）
        if (isEditMode)
        {
            ApplyZOrder();
        }

        Log.Information("編集モード一括切替: {Mode}（対象: {Count}枚）",
            isEditMode ? "ON" : "OFF", _notes.Count);
    }

    // ==========================================
    //  Phase 10: 一時非表示管理（FR-HIDE）
    // ==========================================

    /// <summary>
    /// 全付箋の一時非表示を切り替える（FR-HIDE-1）
    /// hidden=true:  全付箋を Cloak（VD 関係なく全部隠す）
    /// hidden=false: 現在 VD の付箋のみ Uncloak して再表示
    /// FR-HIDE-3: 編集ON中に非表示を押した場合 → 強制的に編集OFFにして非表示
    /// </summary>
    public void SetHidden(bool hidden)
    {
        // FR-HIDE-3: 編集ON中に非表示 → 強制的に編集OFF
        if (hidden && IsEditMode)
        {
            SetEditMode(false);
        }

        _appSettings.IsHidden = hidden;

        if (hidden)
        {
            // 全付箋を Cloak（VD 関係なく全部）
            foreach (var (_, window) in _notes)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    VirtualDesktopService.CloakWindow(hwnd);
                }
            }
            Log.Information("一時非表示: ON（{Count}枚を隠しました）", _notes.Count);
        }
        else
        {
            // FR-HIDE-3: 再表示しても編集はOFFのまま
            // 現在 VD の付箋のみ Uncloak
            var currentDesktop = _vdService.IsAvailable ? _vdService.GetCurrentDesktopIdFast() : null;

            foreach (var (model, window) in _notes)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) continue;

                var belongsHere = model.DesktopId == Guid.Empty
                    || !currentDesktop.HasValue
                    || model.DesktopId == currentDesktop.Value;

                if (belongsHere)
                {
                    VirtualDesktopService.UncloakWindow(hwnd);
                }
                // 非現在VDの付箋は Cloak のまま（HandleDesktopSwitch が管理する）
            }

            // Z順を再適用
            ApplyZOrder();

            Log.Information("一時非表示: OFF（現在VDの付箋を再表示）");
        }

        _persistence.ScheduleSave();
    }

    // ==========================================
    //  選択状態管理（Phase 3）
    // ==========================================

    /// <summary>
    /// 指定された付箋を選択する（他の付箋の選択は解除）
    /// </summary>
    public void SelectNote(Guid noteId)
    {
        if (SelectedNoteId == noteId) return;

        // 全付箋の選択を解除
        foreach (var (_, window) in _notes)
        {
            window.SetSelected(window.Model.NoteId == noteId);
        }

        SelectedNoteId = noteId;
    }

    /// <summary>
    /// 全付箋の選択を解除する
    /// </summary>
    public void DeselectAll()
    {
        foreach (var (_, window) in _notes)
        {
            window.SetSelected(false);
        }
        SelectedNoteId = null;
    }

    // ==========================================
    //  Phase 5: 永続化（SaveAll / LoadAll）
    // ==========================================

    /// <summary>
    /// 全データを保存する（notes.json + 全RTF + settings.json）
    /// PersistenceService の SaveRequested イベントから呼ばれる（デバウンス / フラッシュ）
    /// </summary>
    public void SaveAll()
    {
        try
        {
            // 1. 全ウィンドウの現在位置・サイズを Model に同期
            foreach (var (model, window) in _notes)
            {
                window.SyncModelFromWindow();
                model.FirstLinePreview = window.GetFirstLinePreview();
            }

            // 2. notes.json 保存（全 NoteModel のメタデータ）
            var notesData = new NotesData
            {
                Notes = _notes.Select(n => n.Model).ToList()
            };
            _persistence.SaveNotesData(notesData);

            // 3. 全 RTF ファイル保存
            foreach (var (model, window) in _notes)
            {
                var rtfBytes = window.GetRtfBytes();
                _persistence.SaveRtf(model.NoteId, rtfBytes);
            }

            // 4. settings.json 保存
            _persistence.SaveSettings(_appSettings);

            Log.Debug("全データ保存完了（付箋数: {Count}）", _notes.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SaveAll 中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 保存データからすべての付箋を復元する（起動時に1回呼ぶ）
    /// FR-BOOT-2: 起動直後は必ず編集OFF（非干渉モード）
    /// </summary>
    public void LoadAll()
    {
        // 1. settings.json 読み込み
        _appSettings = _persistence.LoadSettings() ?? new AppSettings();

        // Phase 10 修正: 旧デフォルト値を新デフォルト (Ctrl+Shift+Alt+E) にマイグレーション
        if (_appSettings.Hotkey.Modifiers == 0x0003 || _appSettings.Hotkey.Modifiers == 0x000A)
        {
            var old = _appSettings.Hotkey.Modifiers;
            _appSettings.Hotkey.Modifiers = 0x0007; // MOD_ALT | MOD_CONTROL | MOD_SHIFT
            Log.Information("ホットキー修飾キーを修正: 0x{Old:X4} → 0x0007 (Ctrl+Shift+Alt)", old);
        }

        Log.Information("設定読み込み完了（IsHidden={IsHidden}）", _appSettings.IsHidden);

        // 2. notes.json 読み込み
        var notesData = _persistence.LoadNotesData();
        if (notesData == null || notesData.Notes.Count == 0)
        {
            Log.Information("保存された付箋がありません（初回起動）");
            return;
        }

        // 3. 各付箋を復元
        foreach (var model in notesData.Notes)
        {
            RestoreNote(model);
        }

        // 4. P8-6: デスクトップ喪失フォールバック — 存在しない VD に所属する付箋を現在VDに救済
        RescueOrphanedNotes();

        // 5. Phase 9: 全 VD の Z順リストを実際の付箋と同期 + Z順を適用
        SyncAllZOrderLists();
        ApplyZOrder();

        // 6. 孤立 RTF ファイルの掃除
        var validIds = new HashSet<Guid>(_notes.Select(n => n.Model.NoteId));
        _persistence.CleanupOrphanedRtfFiles(validIds);

        // 7. Phase 10: 非表示状態の復元（FR-HIDE-2: 非表示状態は再起動後も維持）
        if (_appSettings.IsHidden)
        {
            foreach (var (_, window) in _notes)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    VirtualDesktopService.CloakWindow(hwnd);
                }
            }
            Log.Information("非表示状態を復元しました（全{Count}枚を Cloak）", _notes.Count);
        }

        Log.Information("付箋を復元しました（{Count}枚, IsHidden={IsHidden}）", _notes.Count, _appSettings.IsHidden);
    }

    /// <summary>
    /// 保存された NoteModel から NoteWindow を復元する
    /// </summary>
    private NoteWindow RestoreNote(NoteModel model)
    {
        // DJ-7/DJ-8: ウィンドウは必ず「クリック透過なし」で生成
        var window = new NoteWindow(model, clickThrough: false);

        // DJ-7: オーナーウィンドウを設定
        if (_ownerWindow != null)
        {
            window.Owner = _ownerWindow;
        }

        // イベント購読
        WireUpNoteEvents(window);

        _notes.Add((model, window));
        window.Show();

        // Phase 6: フォント許可リストを設定 + スタイル適用
        window.SetFontAllowList(_appSettings.FontAllowList);
        window.ApplyStyle();

        // RTF コンテンツの復元
        var rtfBytes = _persistence.LoadRtf(model.NoteId);
        if (rtfBytes != null && rtfBytes.Length > 0)
        {
            window.LoadRtfBytes(rtfBytes);
        }

        // FR-BOOT-2: 起動直後は必ず編集OFF（非干渉モード）
        window.SetClickThrough(true);

        // Phase 8: 復元時に DesktopId をチェックし、現在の VD に属さない付箋は Cloak
        if (_vdService.IsAvailable && model.DesktopId != Guid.Empty)
        {
            var currentDesktop = _vdService.GetCurrentDesktopIdFast();
            if (currentDesktop.HasValue && model.DesktopId != currentDesktop.Value)
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                VirtualDesktopService.CloakWindow(hwnd);
                Log.Information("復元時 Cloak: {NoteId} (VD={DesktopId}, 現在={CurrentVD})",
                    model.NoteId, model.DesktopId, currentDesktop);
            }
        }
        // DesktopId が空の付箋（旧データ）は現在の VD に付替
        if (model.DesktopId == Guid.Empty && _vdService.IsAvailable)
        {
            var currentDesktop = _vdService.GetCurrentDesktopIdFast();
            if (currentDesktop.HasValue)
            {
                model.DesktopId = currentDesktop.Value;
                Log.Information("旧データ VD 付替: {NoteId} → {DesktopId}", model.NoteId, model.DesktopId);
            }
        }

        // 変更追跡を有効化（これ以降の変更がデバウンス保存のトリガーになる）
        window.EnableChangeTracking();

        // FirstLinePreview を更新
        model.FirstLinePreview = window.GetFirstLinePreview();

        Log.Information("付箋を復元: {NoteId} (位置: {X:F0}, {Y:F0}, サイズ: {W:F0}x{H:F0}, VD={DesktopId})",
            model.NoteId,
            model.Placement.DipX, model.Placement.DipY,
            model.Placement.DipWidth, model.Placement.DipHeight,
            model.DesktopId);

        return window;
    }

    // ==========================================
    //  付箋 CRUD
    // ==========================================

    /// <summary>
    /// 新規付箋を作成して表示する
    /// 現在の編集モードに合わせてクリック透過状態を設定する
    /// Phase 5: 作成後に即時保存をスケジュール
    /// </summary>
    public NoteWindow CreateNote()
    {
        var model = new NoteModel();

        // 初期配置: プライマリモニタの WorkArea 中央付近
        var workArea = SystemParameters.WorkArea;
        model.Placement.DipX = workArea.Left + (workArea.Width - model.Placement.DipWidth) / 2;
        model.Placement.DipY = workArea.Top + (workArea.Height - model.Placement.DipHeight) / 2;

        // 重なり検知 + ずらし（既存付箋と完全重複を避ける）
        ApplyOverlapOffset(model, workArea);

        // Phase 8 + Phase 13 BUG 3 修正: 現在のデスクトップ ID を付与（フォールバック付き）
        if (_vdService.IsAvailable)
        {
            var desktopId = _vdService.GetCurrentDesktopIdFast();
            if (!desktopId.HasValue)
            {
                // Phase 13: 高速パスが null → 重量級の短命ウィンドウ方式にフォールバック
                Log.Warning("GetCurrentDesktopIdFast が null — GetCurrentDesktopId にフォールバック");
                desktopId = _vdService.GetCurrentDesktopId();
            }
            if (desktopId.HasValue)
            {
                model.DesktopId = desktopId.Value;
            }
            else
            {
                Log.Warning("CreateNote: DesktopId を取得できません（Guid.Empty のまま = 全VD表示）");
            }
        }

        // DJ-7: ウィンドウは必ず「クリック透過なし」で生成する
        var window = new NoteWindow(model, clickThrough: false);

        // DJ-7: オーナーウィンドウを設定（Alt+Tab 非表示 + 仮想デスクトップ参加）
        if (_ownerWindow != null)
        {
            window.Owner = _ownerWindow;
        }

        // イベント購読
        WireUpNoteEvents(window);

        // Phase 6: フォント許可リストを設定
        window.SetFontAllowList(_appSettings.FontAllowList);

        _notes.Add((model, window));

        // Phase 9: 新規付箋を Z順リストの前面に追加
        AddToZOrder(model);

        window.Show();

        // Phase 10: 非表示ON中は Cloak して見えなくする（仕様6.1）
        if (_appSettings.IsHidden)
        {
            // Show() 後に即 Cloak（非表示中はモデルだけ作成して見せない）
            window.SetClickThrough(true);
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                VirtualDesktopService.CloakWindow(hwnd);
            }
        }
        else
        {
            // DJ-7/DJ-8: Show() 後に実際のモード状態を適用
            if (IsEditMode)
            {
                window.SetInEditMode(true);
            }
            else
            {
                window.SetClickThrough(true);
            }
        }

        // Phase 9: Z順を適用（Show() 後に呼ぶこと）
        ApplyZOrder();

        // Phase 5: 変更追跡を有効化
        window.EnableChangeTracking();

        // Phase 5: 新規作成を即時保存スケジュール
        _persistence.ScheduleSave();

        Log.Information("付箋を作成: {NoteId} (位置: {X:F0}, {Y:F0}, サイズ: {W:F0}x{H:F0}, モード: {Mode}, Hidden={Hidden}, Owner={HasOwner})",
            model.NoteId,
            model.Placement.DipX, model.Placement.DipY,
            model.Placement.DipWidth, model.Placement.DipHeight,
            IsEditMode ? "編集" : "非干渉",
            _appSettings.IsHidden,
            _ownerWindow != null);

        return window;
    }

    /// <summary>
    /// 指定された付箋を複製する（+24px ずらし、最大10回のクランプ付き）
    /// Phase 5: RTF コンテンツもコピーし、複製後に保存をスケジュール
    /// </summary>
    public NoteWindow? DuplicateNote(Guid sourceNoteId)
    {
        var sourceIndex = _notes.FindIndex(n => n.Model.NoteId == sourceNoteId);
        if (sourceIndex < 0)
        {
            Log.Warning("複製元の付箋が見つかりません: {NoteId}", sourceNoteId);
            return null;
        }

        var source = _notes[sourceIndex];
        var model = new NoteModel();

        // サイズをコピー
        model.Placement.DipWidth = source.Model.Placement.DipWidth;
        model.Placement.DipHeight = source.Model.Placement.DipHeight;

        // 位置を +24px ずらし
        model.Placement.DipX = source.Model.Placement.DipX + 24;
        model.Placement.DipY = source.Model.Placement.DipY + 24;

        // Phase 13: BUG 1 修正 — DesktopId を複製元からコピー（未コピーだと Guid.Empty → 全VD表示バグ）
        model.DesktopId = source.Model.DesktopId;

        // スタイルをコピー
        model.Style = new NoteStyle
        {
            BgPaletteCategoryId = source.Model.Style.BgPaletteCategoryId,
            BgColorId = source.Model.Style.BgColorId,
            Opacity0to100 = source.Model.Style.Opacity0to100,
            TextColor = source.Model.Style.TextColor,
            FontFamilyName = source.Model.Style.FontFamilyName,
        };

        // 画面内にクランプ
        var workArea = SystemParameters.WorkArea;
        ClampToWorkArea(model, workArea);

        // DJ-7: 複製も「クリック透過なし」で生成 → Show() 後に適用
        var window = new NoteWindow(model, clickThrough: false);

        // DJ-7: オーナーウィンドウを設定
        if (_ownerWindow != null)
        {
            window.Owner = _ownerWindow;
        }

        // Phase 6: フォント許可リストを設定
        window.SetFontAllowList(_appSettings.FontAllowList);

        // イベント購読
        WireUpNoteEvents(window);

        _notes.Add((model, window));

        // Phase 9: 複製付箋を Z順リストの前面に追加
        AddToZOrder(model);

        window.Show();

        // Phase 5: RTF コンテンツを複製元からコピー
        var sourceRtfBytes = source.Window.GetRtfBytes();
        if (sourceRtfBytes.Length > 0)
        {
            window.LoadRtfBytes(sourceRtfBytes);
        }

        // DJ-7/DJ-8: Show() 後に実際のモード状態を適用
        if (IsEditMode)
        {
            window.SetInEditMode(true);
        }
        else
        {
            window.SetClickThrough(true);
        }

        // 複製された付箋を選択
        if (IsEditMode)
        {
            SelectNote(model.NoteId);
        }

        // Phase 9: Z順を適用（Show() 後に呼ぶこと）
        ApplyZOrder();

        // Phase 5: 変更追跡を有効化 + 保存スケジュール
        window.EnableChangeTracking();
        _persistence.ScheduleSave();

        Log.Information("付箋を複製: {SourceId} → {NewId} (位置: {X:F0}, {Y:F0})",
            sourceNoteId, model.NoteId,
            model.Placement.DipX, model.Placement.DipY);

        return window;
    }

    /// <summary>
    /// 指定された付箋を削除する
    /// Phase 5: RTF ファイルも削除し、保存をスケジュール
    /// </summary>
    public bool DeleteNote(Guid noteId)
    {
        var index = _notes.FindIndex(n => n.Model.NoteId == noteId);
        if (index < 0)
        {
            Log.Warning("削除対象の付箋が見つかりません: {NoteId}", noteId);
            return false;
        }

        var (model, window) = _notes[index];

        // イベント購読解除
        UnwireNoteEvents(window);

        _notes.RemoveAt(index);
        window.Close();

        // Phase 9: Z順リストから除去
        RemoveFromZOrder(noteId);

        // 選択中だった場合は選択をクリア
        if (SelectedNoteId == noteId)
        {
            SelectedNoteId = null;
        }

        // Phase 5: RTF ファイルを削除 + 保存スケジュール
        _persistence.DeleteRtf(noteId);
        _persistence.ScheduleSave();

        Log.Information("付箋を削除: {NoteId}", noteId);
        return true;
    }

    /// <summary>
    /// 全ウィンドウを閉じる（アプリ終了時用）
    /// オーナーウィンドウも閉じる（DJ-7）
    /// </summary>
    public void CloseAllWindows()
    {
        Log.Information("全付箋ウィンドウを閉じます（{Count}枚）", _notes.Count);

        foreach (var (_, window) in _notes.ToList())
        {
            // Owner を解除してから閉じる（Owner が先に閉じると子も連鎖で閉じてしまうため）
            window.Owner = null;
            UnwireNoteEvents(window);
            window.Close();
        }

        _notes.Clear();
        SelectedNoteId = null;

        // オーナーウィンドウを閉じる（DJ-7）
        if (_ownerWindow != null)
        {
            _ownerWindow.Close();
            _ownerWindow = null;
            Log.Information("オーナーウィンドウを閉じました");
        }
    }

    // ==========================================
    //  Phase 8: VD 表示制御
    // ==========================================

    /// <summary>
    /// デスクトップ切替時に付箋の表示/非表示を制御する（DJ-10）
    /// 現在の VD に属する付箋を Uncloak、それ以外を Cloak
    /// ★ P8-6: 孤立付箋（削除された VD に所属）はリアルタイムで現在 VD に救済
    /// ★ 編集モード中は、現在VDの付箋のみ WS_EX_TRANSPARENT を外してクリック可能にする
    ///    非現在VDの付箋は WS_EX_TRANSPARENT を維持して OS の VD 追跡干渉を回避
    /// </summary>
    public void HandleDesktopSwitch(Guid currentDesktopId)
    {
        // Phase 10: 非表示中は VD 切替の表示制御をスキップ（全付箋 Cloak のまま）
        if (_appSettings.IsHidden)
        {
            Log.Debug("VD 切替検知（非表示中のためスキップ）: VD={DesktopId}", currentDesktopId);
            return;
        }

        // Phase 13: BUG 2 修正 — リアルタイム孤立判定を削除
        // VD 切替のたびに Registry を読んで孤立判定すると、Registry の一時的な不整合で
        // 他デスクトップの付箋が誤って現在VDに引っ張られるリスクがある。
        // 孤立救済は起動時の RescueOrphanedNotes() のみに限定する。
        // （後回し.md §1: 「リアルタイム救済は動作しない」記載済み）

        // 通常の Cloak/Uncloak 処理
        var cloakCount = 0;
        var uncloakCount = 0;

        foreach (var (model, window) in _notes)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) continue;

            var belongsHere = model.DesktopId == Guid.Empty || model.DesktopId == currentDesktopId;

            if (belongsHere)
            {
                // 現在の VD に属する → 表示
                VirtualDesktopService.UncloakWindow(hwnd);
                uncloakCount++;

                if (IsEditMode)
                {
                    // 編集中: クリック可能にする
                    window.SetClickThrough(false);
                    window.SetInEditMode(true);
                }
            }
            else
            {
                // 他の VD に属する → 非表示
                if (IsEditMode)
                {
                    // 編集中: WS_EX_TRANSPARENT を戻す（OS VD 追跡回避）+ 編集状態解除
                    window.SetClickThrough(true);
                    window.SetInEditMode(false);
                }
                VirtualDesktopService.CloakWindow(hwnd);
                cloakCount++;
            }
        }

        // 編集中にVD切替した場合、選択をクリア（切替先に選択付箋がない可能性）
        if (IsEditMode && SelectedNoteId.HasValue)
        {
            var selectedExists = _notes.Any(n =>
                n.Model.NoteId == SelectedNoteId.Value &&
                (n.Model.DesktopId == Guid.Empty || n.Model.DesktopId == currentDesktopId));
            if (!selectedExists)
            {
                DeselectAll();
            }
        }

        // Phase 9: 切替先デスクトップの Z順を適用
        ApplyZOrder(currentDesktopId);

        Log.Information("デスクトップ切替処理: VD={DesktopId}, 表示={Uncloak}, 非表示={Cloak}, EditMode={EditMode}",
            currentDesktopId, uncloakCount, cloakCount, IsEditMode);
    }

    // ==========================================
    //  P8-6: デスクトップ喪失フォールバック
    // ==========================================

    /// <summary>
    /// 起動時に孤立付箋を救済する（P8-6）
    /// 保存された DesktopId が現在のシステムに存在しない場合、現在の VD に付替え + Uncloak
    /// </summary>
    private void RescueOrphanedNotes()
    {
        if (!_vdService.IsAvailable) return;

        var desktopIds = _notes
            .Where(n => n.Model.DesktopId != Guid.Empty)
            .Select(n => n.Model.DesktopId)
            .Distinct();

        var orphanedIds = _vdService.FindOrphanedDesktopIds(desktopIds);
        if (orphanedIds.Count == 0) return;

        var currentDesktop = _vdService.GetCurrentDesktopIdFast();
        if (!currentDesktop.HasValue) return;

        var rescuedCount = 0;
        foreach (var (model, window) in _notes)
        {
            if (orphanedIds.Contains(model.DesktopId))
            {
                var oldId = model.DesktopId;
                model.DesktopId = currentDesktop.Value;

                // Cloak されている可能性があるので Uncloak
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    VirtualDesktopService.UncloakWindow(hwnd);
                }

                rescuedCount++;
                Log.Information("P8-6 孤立付箋救済: {NoteId} (旧VD={OldId} → 現在VD={NewId})",
                    model.NoteId, oldId, currentDesktop.Value);
            }
        }

        if (rescuedCount > 0)
        {
            _persistence.ScheduleSave();
            Log.Information("P8-6 孤立付箋救済完了: {Count}枚を現在VDに移動", rescuedCount);
        }
    }

    // ==========================================
    //  Phase 9: Z順管理（FR-ZORDER / DJ-2）
    // ==========================================

    /// <summary>
    /// 指定デスクトップの Z順リストを取得（なければ作成）
    /// </summary>
    private List<Guid> GetOrCreateZOrderList(Guid desktopId)
    {
        if (!_appSettings.ZOrderByDesktop.TryGetValue(desktopId, out var list))
        {
            list = new List<Guid>();
            _appSettings.ZOrderByDesktop[desktopId] = list;
        }
        return list;
    }

    /// <summary>
    /// 付箋を Z順リストの最前面（index 0）に追加する
    /// </summary>
    private void AddToZOrder(NoteModel model)
    {
        if (model.DesktopId == Guid.Empty) return;
        var list = GetOrCreateZOrderList(model.DesktopId);
        list.Remove(model.NoteId); // 既存なら除去
        list.Insert(0, model.NoteId); // 最前面に追加
    }

    /// <summary>
    /// 付箋を全 Z順リストから除去する
    /// </summary>
    private void RemoveFromZOrder(Guid noteId)
    {
        foreach (var list in _appSettings.ZOrderByDesktop.Values)
        {
            list.Remove(noteId);
        }
    }

    /// <summary>
    /// 指定デスクトップの Z順リストを実際の付箋と同期する
    /// （存在しない付箋を除去、リストに漏れた付箋を最前面に追加）
    /// </summary>
    private void SyncZOrderList(Guid desktopId)
    {
        var list = GetOrCreateZOrderList(desktopId);
        var desktopNoteIds = _notes
            .Where(n => n.Model.DesktopId == desktopId)
            .Select(n => n.Model.NoteId)
            .ToHashSet();

        // リストにいるが付箋が存在しないものを除去
        list.RemoveAll(id => !desktopNoteIds.Contains(id));

        // 付箋が存在するがリストにないものを最前面に追加
        foreach (var noteId in desktopNoteIds)
        {
            if (!list.Contains(noteId))
            {
                list.Insert(0, noteId);
            }
        }
    }

    /// <summary>
    /// 全デスクトップの Z順リストを同期する（起動時に呼ぶ）
    /// </summary>
    private void SyncAllZOrderLists()
    {
        var desktopIds = _notes
            .Where(n => n.Model.DesktopId != Guid.Empty)
            .Select(n => n.Model.DesktopId)
            .Distinct()
            .ToList();

        foreach (var desktopId in desktopIds)
        {
            SyncZOrderList(desktopId);
        }

        // 空の Z順リストを掃除
        var emptyKeys = _appSettings.ZOrderByDesktop
            .Where(kv => kv.Value.Count == 0)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in emptyKeys)
        {
            _appSettings.ZOrderByDesktop.Remove(key);
        }
    }

    /// <summary>
    /// 指定デスクトップ（省略時は現在VD）の Z順を SetWindowPos で適用する
    /// ZOrderByDesktop[desktopId] の並び順: index 0 = 最前面、末尾 = 最背面
    /// 適用手順: 末尾→先頭の順に HWND_TOPMOST で配置（最後に配置した窓が最前面）
    /// DJ-2: クリック/アクティブ化で Z順が崩れた場合のリセットにも使用
    /// </summary>
    public void ApplyZOrder(Guid? desktopId = null)
    {
        var targetDesktopId = desktopId
            ?? (_vdService.IsAvailable ? _vdService.GetCurrentDesktopIdFast() : null);

        if (!targetDesktopId.HasValue) return;
        if (!_appSettings.ZOrderByDesktop.TryGetValue(targetDesktopId.Value, out var zOrder)) return;
        if (zOrder.Count == 0) return;

        // 末尾（最背面）→ 先頭（最前面）の順に SetWindowPos
        for (int i = zOrder.Count - 1; i >= 0; i--)
        {
            var noteId = zOrder[i];
            var entry = _notes.FirstOrDefault(n => n.Model.NoteId == noteId);
            if (entry.Window == null) continue;

            var hwnd = new WindowInteropHelper(entry.Window).Handle;
            if (hwnd == IntPtr.Zero) continue;

            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Z順を外部から更新する（Z順管理ウィンドウの D&D 並び替えから呼ばれる）
    /// </summary>
    public void UpdateZOrder(Guid desktopId, List<Guid> orderedNoteIds)
    {
        _appSettings.ZOrderByDesktop[desktopId] = new List<Guid>(orderedNoteIds);
        ApplyZOrder(desktopId);
        _persistence.ScheduleSave();
        Log.Information("Z順を更新: VD={DesktopId}, 付箋数={Count}", desktopId, orderedNoteIds.Count);
    }

    /// <summary>
    /// 現在の仮想デスクトップ ID を取得する（VD 未対応なら null）
    /// </summary>
    public Guid? GetCurrentDesktopId()
    {
        return _vdService.IsAvailable ? _vdService.GetCurrentDesktopIdFast() : null;
    }

    /// <summary>
    /// デスクトップ名を取得する
    /// </summary>
    public string GetDesktopName(Guid desktopId)
    {
        if (!_vdService.IsAvailable) return "デスクトップ";
        var desktops = _vdService.GetDesktopListFromRegistry();
        var match = desktops.FirstOrDefault(d => d.Id == desktopId);
        return !string.IsNullOrEmpty(match.Name) ? match.Name : $"Desktop {desktopId.ToString("N")[..8]}…";
    }

    /// <summary>
    /// 指定デスクトップの付箋を Z順で返す（Z順管理ウィンドウ用）
    /// </summary>
    public List<(Guid NoteId, string Preview, string BgHex)> GetOrderedNotesForDesktop(Guid desktopId)
    {
        SyncZOrderList(desktopId);
        var zOrder = GetOrCreateZOrderList(desktopId);
        var desktopNotes = _notes
            .Where(n => n.Model.DesktopId == desktopId)
            .ToDictionary(n => n.Model.NoteId);

        var result = new List<(Guid, string, string)>();
        foreach (var noteId in zOrder)
        {
            if (desktopNotes.TryGetValue(noteId, out var entry))
            {
                // FirstLinePreview を最新に更新
                entry.Model.FirstLinePreview = entry.Window.GetFirstLinePreview();
                var preview = string.IsNullOrWhiteSpace(entry.Model.FirstLinePreview)
                    ? "（空）" : entry.Model.FirstLinePreview;
                var bgHex = PaletteDefinitions.GetHexColor(
                    entry.Model.Style.BgPaletteCategoryId, entry.Model.Style.BgColorId)
                    ?? PaletteDefinitions.DefaultHexColor;
                result.Add((noteId, preview, bgHex));
            }
        }
        return result;
    }

    // ==========================================
    //  内部ヘルパー
    // ==========================================

    /// <summary>
    /// NoteWindow のイベントを購読する
    /// </summary>
    private void WireUpNoteEvents(NoteWindow window)
    {
        window.NoteActivated += OnNoteActivated;
        window.DeleteRequested += OnDeleteRequested;
        window.DuplicateRequested += OnDuplicateRequested;
        window.NoteChanged += OnNoteChanged; // Phase 5
    }

    /// <summary>
    /// NoteWindow のイベント購読を解除する
    /// </summary>
    private void UnwireNoteEvents(NoteWindow window)
    {
        window.NoteActivated -= OnNoteActivated;
        window.DeleteRequested -= OnDeleteRequested;
        window.DuplicateRequested -= OnDuplicateRequested;
        window.NoteChanged -= OnNoteChanged; // Phase 5
    }

    private void OnNoteActivated(Guid noteId)
    {
        SelectNote(noteId);
        // Phase 9: DJ-2 — クリック/アクティブ化で Z順を変えない（設定した順序を再適用）
        ApplyZOrder();
    }

    private void OnDeleteRequested(Guid noteId)
    {
        DeleteNote(noteId);
    }

    private void OnDuplicateRequested(Guid noteId)
    {
        DuplicateNote(noteId);
    }

    /// <summary>
    /// Phase 5: 付箋の内容・位置・サイズが変更された → デバウンス保存をスケジュール
    /// </summary>
    private void OnNoteChanged(Guid noteId)
    {
        _persistence.ScheduleSave();
    }

    /// <summary>
    /// 新規作成時の重なり検知 + ずらし（+24px、最大10回）
    /// </summary>
    private void ApplyOverlapOffset(NoteModel model, Rect workArea)
    {
        const double offset = 24;
        const int maxAttempts = 10;

        for (int i = 0; i < maxAttempts; i++)
        {
            bool overlaps = false;
            foreach (var (existing, _) in _notes)
            {
                if (Math.Abs(existing.Placement.DipX - model.Placement.DipX) < 10 &&
                    Math.Abs(existing.Placement.DipY - model.Placement.DipY) < 10)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps) break;

            model.Placement.DipX += offset;
            model.Placement.DipY += offset;

            // クランプ
            ClampToWorkArea(model, workArea);
        }
    }

    /// <summary>
    /// 付箋の位置を WorkArea 内にクランプする
    /// </summary>
    private static void ClampToWorkArea(NoteModel model, Rect workArea)
    {
        model.Placement.DipX = Math.Max(workArea.Left,
            Math.Min(model.Placement.DipX, workArea.Right - model.Placement.DipWidth));
        model.Placement.DipY = Math.Max(workArea.Top,
            Math.Min(model.Placement.DipY, workArea.Bottom - model.Placement.DipHeight));
    }
}
