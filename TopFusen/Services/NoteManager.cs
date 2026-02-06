using System.Windows;
using Serilog;
using TopFusen.Models;
using TopFusen.Views;

namespace TopFusen.Services;

/// <summary>
/// 付箋のライフサイクル管理（生成 / 保持 / 破棄 / モード切替 / 選択管理）
/// Phase 1: メモリ上のみ管理。永続化は Phase 5 で実装。
/// Phase 2: 編集モード管理 + クリック透過の一括制御
/// Phase 3: 選択状態管理 + 複製 + イベント連携
/// </summary>
public class NoteManager
{
    private readonly List<(NoteModel Model, NoteWindow Window)> _notes = new();

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

    // ==========================================
    //  編集モード管理
    // ==========================================

    /// <summary>
    /// 編集モードを切り替え、全付箋ウィンドウに一括反映する
    /// 編集OFF時は選択状態もクリアする
    /// </summary>
    public void SetEditMode(bool isEditMode)
    {
        IsEditMode = isEditMode;
        var clickThrough = !isEditMode;

        foreach (var (_, window) in _notes)
        {
            window.SetClickThrough(clickThrough);
            window.SetInEditMode(isEditMode);
        }

        // 編集OFF → 選択クリア
        if (!isEditMode)
        {
            DeselectAll();
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
    //  付箋 CRUD
    // ==========================================

    /// <summary>
    /// 新規付箋を作成して表示する
    /// 現在の編集モードに合わせてクリック透過状態を設定する
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

        // 現在の編集モードに基づいてクリック透過状態を決定
        var clickThrough = !IsEditMode;
        var window = new NoteWindow(model, clickThrough);

        // イベント購読
        WireUpNoteEvents(window);

        _notes.Add((model, window));
        window.Show();

        Log.Information("付箋を作成: {NoteId} (位置: {X:F0}, {Y:F0}, サイズ: {W:F0}x{H:F0}, モード: {Mode})",
            model.NoteId,
            model.Placement.DipX, model.Placement.DipY,
            model.Placement.DipWidth, model.Placement.DipHeight,
            IsEditMode ? "編集" : "非干渉");

        return window;
    }

    /// <summary>
    /// 指定された付箋を複製する（+24px ずらし、最大10回のクランプ付き）
    /// </summary>
    public NoteWindow? DuplicateNote(Guid sourceNoteId)
    {
        var sourceIndex = _notes.FindIndex(n => n.Model.NoteId == sourceNoteId);
        if (sourceIndex < 0)
        {
            Log.Warning("複製元の付箋が見つかりません: {NoteId}", sourceNoteId);
            return null;
        }

        var source = _notes[sourceIndex].Model;
        var model = new NoteModel();

        // サイズをコピー
        model.Placement.DipWidth = source.Placement.DipWidth;
        model.Placement.DipHeight = source.Placement.DipHeight;

        // 位置を +24px ずらし
        model.Placement.DipX = source.Placement.DipX + 24;
        model.Placement.DipY = source.Placement.DipY + 24;

        // スタイルをコピー
        model.Style = new NoteStyle
        {
            BgPaletteCategoryId = source.Style.BgPaletteCategoryId,
            BgColorId = source.Style.BgColorId,
            Opacity0to100 = source.Style.Opacity0to100,
            TextColor = source.Style.TextColor,
            FontFamilyName = source.Style.FontFamilyName,
        };

        // 画面内にクランプ
        var workArea = SystemParameters.WorkArea;
        ClampToWorkArea(model, workArea);

        var clickThrough = !IsEditMode;
        var window = new NoteWindow(model, clickThrough);

        // イベント購読
        WireUpNoteEvents(window);

        _notes.Add((model, window));
        window.Show();

        // 複製された付箋を選択
        if (IsEditMode)
        {
            SelectNote(model.NoteId);
        }

        Log.Information("付箋を複製: {SourceId} → {NewId} (位置: {X:F0}, {Y:F0})",
            sourceNoteId, model.NoteId,
            model.Placement.DipX, model.Placement.DipY);

        return window;
    }

    /// <summary>
    /// 指定された付箋を削除する
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

        // TODO: Phase 5 で RTF ファイル削除を追加
        Log.Information("付箋を削除: {NoteId}", noteId);
        return true;
    }

    /// <summary>
    /// 全ウィンドウを閉じる（アプリ終了時用）
    /// </summary>
    public void CloseAllWindows()
    {
        Log.Information("全付箋ウィンドウを閉じます（{Count}枚）", _notes.Count);

        foreach (var (_, window) in _notes.ToList())
        {
            UnwireNoteEvents(window);
            window.Close();
        }

        _notes.Clear();
        SelectedNoteId = null;
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
    }

    /// <summary>
    /// NoteWindow のイベント購読を解除する
    /// </summary>
    private void UnwireNoteEvents(NoteWindow window)
    {
        window.NoteActivated -= OnNoteActivated;
        window.DeleteRequested -= OnDeleteRequested;
        window.DuplicateRequested -= OnDuplicateRequested;
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
