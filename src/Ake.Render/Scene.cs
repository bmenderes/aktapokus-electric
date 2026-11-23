using System.Globalization;
using System.Security;
using System.Text;
using Ake.Core.Model;

namespace Ake.Render;

// Sahne: çıktı biçiminden bağımsız çizim listesi. Bütün ölçüler milimetredir, başlangıç noktası
// sayfanın sol üst köşesidir. SVG ve PDF aynı sahneyi çizer.

public abstract record SceneItem;

/// <summary><see cref="Color"/> boşsa siyah; değilse <c>#rrggbb</c>.</summary>
public sealed record LineItem(
    double X1, double Y1, double X2, double Y2, double StrokeMm = 0.25, string? Color = null, LineStyle Style = LineStyle.Solid) : SceneItem;

/// <summary><see cref="Filled"/> doğruysa içi dolu çizilir (tellerin birleştiği düğüm noktası).</summary>
public sealed record CircleItem(
    double X, double Y, double Radius, double StrokeMm = 0.25, bool Filled = false) : SceneItem;

public sealed record RectItem(double X, double Y, double Width, double Height, double StrokeMm = 0.25) : SceneItem;

/// <summary>
/// <see cref="Y"/> metnin taban çizgisidir. <see cref="RotationDeg"/> metni (X, Y) çevresinde döndürür;
/// -90 aşağıdan yukarı okunan dik yazıdır.
/// </summary>
public sealed record TextItem(
    double X,
    double Y,
    string Text,
    double SizeMm,
    string FontFamily,
    TextAlign Align = TextAlign.Left,
    double RotationDeg = 0,
    string? Color = null) : SceneItem;

/// <summary>Dış görsel (logo). Yol proje dosyasının klasörüne göredir; görsel kutuya oranı korunarak sığar.</summary>
public sealed record ImageItem(double X, double Y, double Width, double Height, string Path) : SceneItem;

/// <summary>
/// Tıklanabilir alan: PDF'te <see cref="Target"/> adlı hedefe (başka sahnenin <see cref="Scene.Anchor"/>'ı)
/// gider. Görünmezdir; SVG, PNG ve editör yok sayar.
/// </summary>
public sealed record LinkItem(double X, double Y, double Width, double Height, string Target) : SceneItem;

/// <summary>
/// Çizilecek sayfa. <see cref="Warnings"/> çizimin kendisinde görünmeyen sorunlardır (ör. yolu bulunamayan
/// tel); çıktı alan komut ve editör bunları kullanıcıya gösterir.
/// </summary>
public sealed record Scene(
    double WidthMm, double HeightMm, IReadOnlyList<SceneItem> Items, IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Warnings { get; init; } = Warnings ?? [];

    /// <summary>PDF yer imi (outline) başlığı; boşsa sahnenin yer imi olmaz.</summary>
    public string? Bookmark { get; init; }

    /// <summary>PDF'te bu sahnenin hedef adı; <see cref="LinkItem"/> buraya bağlanır.</summary>
    public string? Anchor { get; init; }
}

/// <summary>Sahneyi SVG metnine çevirir. Çıktı deterministiktir; snapshot testleri bunu kullanır.</summary>
public static class SvgExporter
{
    // Çizgi biçimi SVG'de stroke-dasharray ile; düz çizgide öznitelik yazılmaz (mevcut snapshot'lar değişmez).
    private static string DashAttribute(LineStyle style, double stroke) => style switch
    {
        LineStyle.Dashed => " stroke-dasharray=\"3 1.5\"",
        LineStyle.DashDot => " stroke-dasharray=\"4 1 0.8 1\"",
        LineStyle.Dotted => " stroke-dasharray=\"0.6 1\"",
        _ => string.Empty,
    };

    public static string Write(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(scene.WidthMm)}mm\" height=\"{N(scene.HeightMm)}mm\" viewBox=\"0 0 {N(scene.WidthMm)} {N(scene.HeightMm)}\">\n");
        svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{N(scene.WidthMm)}\" height=\"{N(scene.HeightMm)}\" fill=\"white\"/>\n");

        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case LineItem line:
                    svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{N(line.X1)}\" y1=\"{N(line.Y1)}\" x2=\"{N(line.X2)}\" y2=\"{N(line.Y2)}\" stroke=\"{line.Color ?? "black"}\" stroke-width=\"{N(line.StrokeMm)}\"{DashAttribute(line.Style, line.StrokeMm)}/>\n");
                    break;
                case CircleItem circle:
                    svg.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{N(circle.X)}\" cy=\"{N(circle.Y)}\" r=\"{N(circle.Radius)}\" fill=\"{(circle.Filled ? "black" : "none")}\" stroke=\"black\" stroke-width=\"{N(circle.StrokeMm)}\"/>\n");
                    break;
                case RectItem rect:
                    svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{N(rect.X)}\" y=\"{N(rect.Y)}\" width=\"{N(rect.Width)}\" height=\"{N(rect.Height)}\" fill=\"none\" stroke=\"black\" stroke-width=\"{N(rect.StrokeMm)}\"/>\n");
                    break;
                case TextItem text:
                    var anchor = text.Align switch
                    {
                        TextAlign.Center => "middle",
                        TextAlign.Right => "end",
                        _ => "start",
                    };
                    var rotate = text.RotationDeg == 0
                        ? string.Empty
                        : string.Create(CultureInfo.InvariantCulture, $" transform=\"rotate({N(text.RotationDeg)} {N(text.X)} {N(text.Y)})\"");
                    svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{N(text.X)}\" y=\"{N(text.Y)}\" font-family=\"{Escape(text.FontFamily)}\" font-size=\"{N(text.SizeMm)}\" text-anchor=\"{anchor}\"{(text.Color is null ? string.Empty : $" fill=\"{text.Color}\"")}>{Escape(text.Text)}</text>\n");
                    break;
                case ImageItem image:
                    svg.Append(CultureInfo.InvariantCulture, $"<image x=\"{N(image.X)}\" y=\"{N(image.Y)}\" width=\"{N(image.Width)}\" height=\"{N(image.Height)}\" href=\"{Escape(image.Path)}\" preserveAspectRatio=\"xMidYMid meet\"/>\n");
                    break;
            }
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static string N(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string text) => SecurityElement.Escape(text) ?? string.Empty;
}
