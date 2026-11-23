namespace Ake.Core.Model;

/// <summary>
/// Hazır sayfa şablonları (ADR-0049): A4, A3 ve A2 kâğıt boyutları, yatay ve dikey. Her şablonun kimliği sabittir; böylece aynı şablon farklı
/// projelerde aynı şablon olarak tanınır. Yatay şablonlar A3'ün antetini kâğıt genişliğine ölçekler; dikey şablonlar dar kâğıt için üç satırlı antet
/// kullanır. Antet içeriği (proje, başlık, çizim no, tarih, çizen, revizyon, sayfa…) hepsinde aynıdır.
/// </summary>
public static class StandardSheetTemplates
{
    private const double Margin = 10;
    private const double Band = 5;

    /// <summary>A3 yatay, 10 kolon: uygulamanın varsayılan şablonu (kimliği ve ölçüleri <c>examples/templates/a3-yatay.sablon.json</c> ile aynıdır).</summary>
    public static SheetTemplate A3Landscape { get; } = Wide("a301", "A3 yatay, 10 kolon", 420, 297, 10, 6);

    /// <summary>Hazır şablonların hepsi, kağıt boyutuna göre: A4, A3, A2; her boyutta önce yatay.</summary>
    public static IReadOnlyList<SheetTemplate> All { get; } =
    [
        Wide("a401", "A4 yatay, 8 kolon", 297, 210, 8, 6),
        Narrow("a402", "A4 dikey, 6 kolon", 210, 297, 6, 8),
        A3Landscape,
        Narrow("a302", "A3 dikey, 8 kolon", 297, 420, 8, 10),
        Wide("a201", "A2 yatay, 14 kolon", 594, 420, 14, 8),
    ];

    /// <summary>Kimliğine göre hazır şablon; hazır şablon değilse <c>null</c>.</summary>
    public static SheetTemplate? Find(SheetTemplateId id) => All.FirstOrDefault(template => template.Id == id);

    private static SheetTemplateId IdOf(string suffix) => new(Guid.Parse($"0193b000-0000-7000-8000-00000000{suffix}"));

    // Yatay kâğıt: A3'ün (400 mm iç genişlik) 20 mm'lik antet yerleşimi, iç genişliğe göre ölçeklenir.
    private static SheetTemplate Wide(string id, string name, double width, double height, int columns, int rows)
    {
        var inner = width - (2 * Margin);
        var k = inner / 400;
        TitleBlockCell Cell(double x, double y, double w, double h, string? label, string? content, double size = 2.5, TextAlign align = TextAlign.Left) =>
            new(Round(x * k), y, Round(w * k), h, label, content, size, align);

        TitleBlockCell[] cells =
        [
            new(0, 0, Round(70 * k), 20, ImageRef: "logo.png"),
            Cell(70, 0, 130, 20, null, "{page.title}", 3.5),
            Cell(200, 0, 70, 10, "Proje:", "{project.name}"),
            Cell(200, 10, 70, 10, "Tarih:", "{project.date}"),
            Cell(270, 0, 50, 10, "Çizim no:", "{project.drawingNo}"),
            Cell(270, 10, 25, 10, "Fonksiyon:", "{page.function}"),
            Cell(295, 10, 25, 10, "Yer:", "{page.location}"),
            Cell(320, 0, 20, 10, "Çizen:", "{project.drawnBy}"),
            Cell(340, 0, 25, 10, "Rev.:", "{project.revision}"),
            Cell(365, 0, 35, 10, "Sayfa:", "{page.number}", 4, TextAlign.Right),
            Cell(320, 10, 40, 10, "Toplam sayfa:", "{page.count}", 2.5, TextAlign.Right),
            Cell(360, 10, 40, 10, "Sonraki sayfa:", "{page.next}", 2.5, TextAlign.Right),
        ];
        return new SheetTemplate(new SheetTemplateId(IdGuid(id)), name, width, height, Margin, columns, rows, Band, 20, "Arial", cells);
    }

    // Dikey kâğıt: 30 mm'lik, üç satırlı dar antet. Solda logo, sağda üç satır: proje/çizim no, başlık/tarih/revizyon, fonksiyon/yer/çizen/toplam/sayfa.
    private static SheetTemplate Narrow(string id, string name, double width, double height, int columns, int rows)
    {
        var inner = width - (2 * Margin);
        var logo = Round(inner * 0.2);
        var rest = inner - logo;
        double X(double fraction) => Round(logo + (rest * fraction));
        double W(double fraction) => Round(rest * fraction);
        TitleBlockCell Cell(double x, int row, double w, string label, string content, double size = 2.5, TextAlign align = TextAlign.Left) =>
            new(X(x), row * 10, W(w), 10, label, content, size, align);

        TitleBlockCell[] cells =
        [
            new(0, 0, logo, 30, ImageRef: "logo.png"),
            Cell(0, 0, 0.6, "Proje:", "{project.name}"),
            Cell(0.6, 0, 0.4, "Çizim no:", "{project.drawingNo}"),
            Cell(0, 1, 0.6, "Başlık:", "{page.title}", 3.5),
            Cell(0.6, 1, 0.2, "Tarih:", "{project.date}"),
            Cell(0.8, 1, 0.2, "Rev.:", "{project.revision}"),
            Cell(0, 2, 0.2, "Fonksiyon:", "{page.function}"),
            Cell(0.2, 2, 0.2, "Yer:", "{page.location}"),
            Cell(0.4, 2, 0.2, "Çizen:", "{project.drawnBy}"),
            Cell(0.6, 2, 0.2, "Toplam sayfa:", "{page.count}", 2.5, TextAlign.Right),
            Cell(0.8, 2, 0.2, "Sayfa:", "{page.number}", 4, TextAlign.Right),
        ];
        return new SheetTemplate(new SheetTemplateId(IdGuid(id)), name, width, height, Margin, columns, rows, Band, 30, "Arial", cells);
    }

    private static Guid IdGuid(string suffix) => IdOf(suffix).Value;

    private static double Round(double value) => Math.Round(value, 2);
}
