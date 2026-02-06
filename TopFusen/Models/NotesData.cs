namespace TopFusen.Models;

/// <summary>
/// notes.json のルートオブジェクト
/// </summary>
public class NotesData
{
    /// <summary>全付箋のメタデータリスト</summary>
    public List<NoteModel> Notes { get; set; } = new();
}
