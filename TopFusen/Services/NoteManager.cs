using System.Windows;
using Serilog;
using TopFusen.Models;
using TopFusen.Views;

namespace TopFusen.Services;

/// <summary>
/// 付箋のライフサイクル管理（生成 / 保持 / 破棄 / モード切替）
/// Phase 1: メモリ上のみ管理。永続化は Phase 5 で実装。
/// Phase 2: 編集モード管理 + クリック透過の一括制御
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

    /// <summary>
    /// 編集モードを切り替え、全付箋ウィンドウに一括反映する
    /// </summary>
    /// <param name="isEditMode">true: 編集モード ON, false: 非干渉モード</param>
    public void SetEditMode(bool isEditMode)
    {
        IsEditMode = isEditMode;
        var clickThrough = !isEditMode;

        foreach (var (_, window) in _notes)
        {
            window.SetClickThrough(clickThrough);
        }

        Log.Information("編集モード一括切替: {Mode}（対象: {Count}枚）",
            isEditMode ? "ON" : "OFF", _notes.Count);
    }

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

        // 現在の編集モードに基づいてクリック透過状態を決定
        var clickThrough = !IsEditMode;
        var window = new NoteWindow(model, clickThrough);
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
        _notes.RemoveAt(index);
        window.Close();

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
            window.Close();
        }

        _notes.Clear();
    }
}
