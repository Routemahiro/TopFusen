namespace TopFusen.Models;

/// <summary>
/// カラーパレット定義（データ駆動）
/// Phase 6: FR-STYLE-1 — ビビッド8色 + ナチュラル8色
/// </summary>
public static class PaletteDefinitions
{
    /// <summary>全カテゴリのパレット定義</summary>
    public static readonly IReadOnlyList<PaletteCategory> Categories = new List<PaletteCategory>
    {
        new("vivid", "ビビッド", new List<PaletteColor>
        {
            new("yellow",  "イエロー",   "#FBE38C"),
            new("orange",  "オレンジ",   "#FFB74D"),
            new("red",     "レッド",     "#EF9A9A"),
            new("pink",    "ピンク",     "#F48FB1"),
            new("purple",  "パープル",   "#CE93D8"),
            new("blue",    "ブルー",     "#90CAF9"),
            new("teal",    "ティール",   "#80CBC4"),
            new("green",   "グリーン",   "#A5D6A7"),
        }),
        new("natural", "ナチュラル", new List<PaletteColor>
        {
            new("cream",    "クリーム",   "#FFF8E1"),
            new("peach",    "ピーチ",     "#FFE0B2"),
            new("rose",     "ローズ",     "#FFCDD2"),
            new("lavender", "ラベンダー", "#E1BEE7"),
            new("sky",      "スカイ",     "#BBDEFB"),
            new("mint",     "ミント",     "#C8E6C9"),
            new("sand",     "サンド",     "#D7CCC8"),
            new("gray",     "グレー",     "#CFD8DC"),
        }),
    };

    /// <summary>
    /// カテゴリIDと色IDから Hex カラーコード（#RRGGBB）を取得する
    /// </summary>
    public static string? GetHexColor(string categoryId, string colorId)
    {
        var category = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null) return null;
        var color = category.Colors.FirstOrDefault(c => c.Id == colorId);
        return color?.HexColor;
    }

    /// <summary>デフォルトの背景色（ビビッド/イエロー）</summary>
    public static string DefaultHexColor => "#FBE38C";
}

/// <summary>パレットカテゴリ（例: ビビッド、ナチュラル）</summary>
public record PaletteCategory(string Id, string DisplayName, IReadOnlyList<PaletteColor> Colors);

/// <summary>パレット内の1色</summary>
public record PaletteColor(string Id, string DisplayName, string HexColor);
