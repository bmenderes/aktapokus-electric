using Ake.Core.Model;

namespace Ake.Editor;

/// <summary>IEC 60757 tel/kablo damarı ana renk kartelası.</summary>
public sealed record WireColorSwatch(string Code, string Hex, string NameTr, string NameEn);

/// <summary>Tel ve damar renk kodlarını renk kartelasına ve hex renge çevirir.</summary>
public static class WireColorPalette
{
    public static IReadOnlyList<WireColorSwatch> Standard { get; } =
    [
        new("BK", "#111111", "Siyah", "Black"),
        new("BN", "#7A4A21", "Kahverengi", "Brown"),
        new("RD", "#D62828", "Kırmızı", "Red"),
        new("OG", "#E8780C", "Turuncu", "Orange"),
        new("YE", "#D4A017", "Sarı", "Yellow"),
        new("GN", "#2E9E2E", "Yeşil", "Green"),
        new("GNYE", "#2E9E2E", "Yeşil-Sarı (PE)", "Green-Yellow (PE)"),
        new("BU", "#1F4FD8", "Mavi", "Blue"),
        new("VT", "#7B2CBF", "Mor", "Violet"),
        new("GY", "#808080", "Gri", "Gray"),
        new("WH", "#B0B0B0", "Beyaz / Açık gri", "White / Light gray"),
        new("PK", "#D63384", "Pembe", "Pink"),
        new("TQ", "#008B8B", "Turkuaz", "Turquoise"),
    ];

    public static string ResolveHex(string? codeOrHex)
    {
        if (string.IsNullOrWhiteSpace(codeOrHex))
        {
            return "#111111";
        }

        var trimmed = codeOrHex.Trim();
        if (trimmed.Length == 7 && trimmed[0] == '#' && trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return trimmed.ToUpperInvariant();
        }

        var match = Standard.FirstOrDefault(s => string.Equals(s.Code, trimmed, StringComparison.OrdinalIgnoreCase));
        return match?.Hex ?? (trimmed.Equals("DBU", StringComparison.OrdinalIgnoreCase) ? "#1F4FD8" : "#111111");
    }

    public static string LineStyleLabel(LineStyle style) => style switch
    {
        LineStyle.Dashed => UiStrings.T("Kesik çizgili"),
        LineStyle.DashDot => UiStrings.T("Çizgi-nokta"),
        LineStyle.Dotted => UiStrings.T("Noktalı"),
        _ => UiStrings.T("Düz çizgi"),
    };
}
