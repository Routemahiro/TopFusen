namespace TopFusen.Models;

/// <summary>
/// アプリケーション全体の設定
/// </summary>
public class AppSettings
{
    /// <summary>付箋の一時非表示状態（永続化される）</summary>
    public bool IsHidden { get; set; }

    /// <summary>ホットキー設定</summary>
    public HotkeySettings Hotkey { get; set; } = new();

    /// <summary>フォント許可リスト</summary>
    public List<string> FontAllowList { get; set; } = new()
    {
        "Yu Gothic UI",
        "Meiryo UI",
        "MS Gothic",
        "MS Mincho",
        "Segoe UI",
        "Arial",
        "Consolas",
        "BIZ UDGothic",
        "BIZ UDMincho"
    };

    /// <summary>仮想デスクトップ単位の Z順（キー: DesktopId, 値: NoteId のリスト（上=前面））</summary>
    public Dictionary<Guid, List<Guid>> ZOrderByDesktop { get; set; } = new();

    /// <summary>自動起動の有効/無効</summary>
    public bool AutoStartEnabled { get; set; }
}

/// <summary>
/// ホットキー設定
/// </summary>
public class HotkeySettings
{
    /// <summary>ホットキーの有効/無効</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>修飾キー（Win32 MOD_* フラグの組み合わせ）</summary>
    public int Modifiers { get; set; } = 0x0003; // MOD_CONTROL | MOD_WIN

    /// <summary>仮想キーコード</summary>
    public int Key { get; set; } = 0x45; // 'E'
}
