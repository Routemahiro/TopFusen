using System.Windows;
using System.Windows.Interop;
using Serilog;
using TopFusen.Models;
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

        Log.Information("編集モード一括切替: {Mode}（対象: {Count}枚）",
            isEditMode ? "ON" : "OFF", _notes.Count);
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

        // 5. 孤立 RTF ファイルの掃除
        var validIds = new HashSet<Guid>(_notes.Select(n => n.Model.NoteId));
        _persistence.CleanupOrphanedRtfFiles(validIds);

        Log.Information("付箋を復元しました（{Count}枚）", _notes.Count);
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

        // Phase 8: 現在のデスクトップ ID を付与
        if (_vdService.IsAvailable)
        {
            var desktopId = _vdService.GetCurrentDesktopIdFast();
            if (desktopId.HasValue)
            {
                model.DesktopId = desktopId.Value;
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
        window.Show();

        // DJ-7/DJ-8: Show() 後に実際のモード状態を適用
        if (IsEditMode)
        {
            window.SetInEditMode(true);
        }
        else
        {
            window.SetClickThrough(true);
        }

        // Phase 5: 変更追跡を有効化
        window.EnableChangeTracking();

        // Phase 5: 新規作成を即時保存スケジュール
        _persistence.ScheduleSave();

        Log.Information("付箋を作成: {NoteId} (位置: {X:F0}, {Y:F0}, サイズ: {W:F0}x{H:F0}, モード: {Mode}, Owner={HasOwner})",
            model.NoteId,
            model.Placement.DipX, model.Placement.DipY,
            model.Placement.DipWidth, model.Placement.DipHeight,
            IsEditMode ? "編集" : "非干渉",
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
        // P8-6: VD 切替時に孤立付箋をリアルタイム救済
        //  （VD が削除された直後の切替イベントで検知して、即座に現在VDに付替える）
        var orphanedIds = _vdService.FindOrphanedDesktopIds(
            _notes.Where(n => n.Model.DesktopId != Guid.Empty)
                  .Select(n => n.Model.DesktopId)
                  .Distinct());

        var rescuedCount = 0;
        if (orphanedIds.Count > 0)
        {
            foreach (var (model, _) in _notes)
            {
                if (orphanedIds.Contains(model.DesktopId))
                {
                    Log.Information("P8-6 リアルタイム救済: {NoteId} (旧VD={OldId} → 現在VD={NewId})",
                        model.NoteId, model.DesktopId, currentDesktopId);
                    model.DesktopId = currentDesktopId;
                    rescuedCount++;
                }
            }
            if (rescuedCount > 0)
            {
                _persistence.ScheduleSave();
            }
        }

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

        Log.Information("デスクトップ切替処理: VD={DesktopId}, 表示={Uncloak}, 非表示={Cloak}, 救済={Rescued}, EditMode={EditMode}",
            currentDesktopId, uncloakCount, cloakCount, rescuedCount, IsEditMode);
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
