using System.Text.Json.Serialization;

namespace TopFusen.Models;

/// <summary>
/// 付箋1枚のデータモデル
/// </summary>
public class NoteModel
{
    /// <summary>付箋の不変ID（GUID）</summary>
    public Guid NoteId { get; set; } = Guid.NewGuid();

    /// <summary>RTF本文のファイルパス（相対: notes/{NoteId}.rtf）</summary>
    public string RtfFileName => $"{NoteId}.rtf";

    /// <summary>所属する仮想デスクトップのID</summary>
    public Guid DesktopId { get; set; } = Guid.Empty;

    /// <summary>モニタ識別情報</summary>
    public MonitorIdentity Monitor { get; set; } = new();

    /// <summary>配置情報</summary>
    public NotePlacement Placement { get; set; } = new();

    /// <summary>見た目スタイル</summary>
    public NoteStyle Style { get; set; } = new();

    /// <summary>1行目プレビュー（Z順一覧表示用、保存しない）</summary>
    [JsonIgnore]
    public string FirstLinePreview { get; set; } = string.Empty;
}

/// <summary>
/// モニタ識別情報（復元時のモニタ照合に使用）
/// </summary>
public class MonitorIdentity
{
    /// <summary>DisplayConfig の monitorDevicePath（最も安定した識別子）</summary>
    public string? DevicePath { get; set; }

    /// <summary>MONITORINFOEX.szDevice（フォールバック用）</summary>
    public string? NameFallback { get; set; }
}

/// <summary>
/// 付箋の配置情報（DJ-3: Relative主 + DIP+DpiScale補助）
/// </summary>
public class NotePlacement
{
    /// <summary>モニタ WorkArea 内の相対X座標（0.0〜1.0）</summary>
    public double RelativeX { get; set; }

    /// <summary>モニタ WorkArea 内の相対Y座標（0.0〜1.0）</summary>
    public double RelativeY { get; set; }

    /// <summary>DIP（Device Independent Pixels）でのX座標</summary>
    public double DipX { get; set; }

    /// <summary>DIP でのY座標</summary>
    public double DipY { get; set; }

    /// <summary>DIP での幅</summary>
    public double DipWidth { get; set; } = 240;

    /// <summary>DIP での高さ</summary>
    public double DipHeight { get; set; } = 180;

    /// <summary>保存時のDPIスケール（復元時の変換用）</summary>
    public double DpiScale { get; set; } = 1.0;
}

/// <summary>
/// 付箋の見た目スタイル
/// </summary>
public class NoteStyle
{
    /// <summary>背景パレットカテゴリID（例: "vivid", "natural"）</summary>
    public string BgPaletteCategoryId { get; set; } = "vivid";

    /// <summary>背景色ID（パレット内の色識別子）</summary>
    public string BgColorId { get; set; } = "yellow";

    /// <summary>不透明度 0〜100（0=完全透明、100=完全不透明）</summary>
    public int Opacity0to100 { get; set; } = 100;

    /// <summary>文字色（null=デフォルト黒）</summary>
    public string? TextColor { get; set; }

    /// <summary>フォントファミリー名（付箋単位）</summary>
    public string FontFamilyName { get; set; } = "Yu Gothic UI";

    /// <summary>垂直テキスト配置（付箋単位）— "top" or "center"（Phase 15）</summary>
    public string VerticalTextAlignment { get; set; } = "top";
}
