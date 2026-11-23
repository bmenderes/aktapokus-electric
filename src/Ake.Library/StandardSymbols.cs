using System.Security.Cryptography;
using System.Text;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Library;

/// <summary>
/// Editörle birlikte gelen standart komponent seti (ADR-0035): kumanda, koruma, röle, motor, güç, klemens ve konnektör, sensör,
/// otomasyon. Semboller bu projeye özgü, elle çizilmiştir (üçüncü taraf koleksiyondan alınmamıştır); IEC 60617'nin sadeleştirilmiş
/// biçimine yakındır. Kimlikler addan türetilir, bu yüzden her kurulumda aynı sembol güncellenir, çoğalmaz.
/// </summary>
public static partial class StandardSymbols
{
    public const string Source = "aktapokus-electric standart seti";
    public const string License = "Apache-2.0 (aktapokus-electric ile birlikte; kendi çizimimiz)";

    private static readonly PointAttributes Power = new(PinKind.Power);
    private static readonly PointAttributes Signal = new(PinKind.Signal);
    private static readonly PointAttributes Earth = new(PinKind.ProtectiveEarth);

    private static readonly Lazy<IReadOnlyList<LibrarySymbol>> Catalog = new(Build);

    /// <summary>Bütün standart semboller, kategorileriyle.</summary>
    public static IReadOnlyList<LibrarySymbol> All => Catalog.Value;

    /// <summary>
    /// Standart seti kütüphane dosyasına koyar; dosya yoksa oluşturur. Aynı kimlikli semboller güncellenir; kullanıcının kendi
    /// sembollerine dokunulmaz.
    /// </summary>
    public static Result EnsureInstalled(string libraryPath)
    {
        ArgumentNullException.ThrowIfNull(libraryPath);

        var opened = ComponentLibrary.Open(libraryPath);
        if (opened.IsFailure)
        {
            return Result.Failure(opened.Error!);
        }

        using var library = opened.Value;
        var imported = library.ImportSymbols(All);
        if (imported.IsFailure)
        {
            return Result.Failure(imported.Error!);
        }

        var products = library.Import(StandardProducts.All);
        return products.IsFailure ? Result.Failure(products.Error!) : Result.Success();
    }

    // ---- yardımcılar

    private static SymbolPin Pin(double x, double y, int dx, int dy, string? name = null, PointAttributes attributes = default) =>
        new(x, y, dx, dy, name, attributes);

    private static SymbolShape Line(double x1, double y1, double x2, double y2, double stroke = 0.25) => new SymbolLine(x1, y1, x2, y2, stroke);

    private static SymbolShape Rect(double x, double y, double w, double h) => new SymbolRect(x, y, w, h);

    private static SymbolShape Circle(double x, double y, double r, bool filled = false) => new SymbolCircle(x, y, r, 0.25, filled);

    private static SymbolShape Text(double x, double y, string text, double size = 3, TextAlign align = TextAlign.Center) =>
        new SymbolText(x, y, text, size, align);

    private static SymbolPin[] VerticalPins(string top = "1", string bottom = "2", PointAttributes? attributes = null) =>
    [
        Pin(0, 0, 0, -1, top, attributes ?? Signal),
        Pin(0, 15, 0, 1, bottom, attributes ?? Signal),
    ];

    private static readonly SymbolShape[] NoContact = [Line(0, 0, 0, 5), Line(0, 10, -3, 4.5), Line(0, 10, 0, 15)];

    private static readonly SymbolShape[] NcContact = [Line(0, 0, 0, 5), Line(0, 5, 2.5, 5), Line(0, 10, 2.6, 4.2), Line(0, 10, 0, 15)];

    private static SymbolShape[] With(SymbolShape[] baseShapes, params SymbolShape[] extra) => [.. baseShapes, .. extra];

    private static LibrarySymbol Def(
        string category, string name, string tag, string deviceName, SymbolShape[] shapes, SymbolPin[] pins) =>
        new(
            new SymbolDefinition(IdOf(name), name, shapes, pins, Source, License, deviceName, tag),
            category);

    private static SymbolId IdOf(string name) =>
        new(new Guid(SHA1.HashData(Encoding.UTF8.GetBytes("aktapokus.standart/" + name))[..16]));

    private static IReadOnlyList<LibrarySymbol> Build()
    {
        // Basma düğmesi gibi elemanların gövdesi: kontak + sola uzanan tahrik kolu.
        SymbolShape[] stem = [Line(-3, 7.5, -8, 7.5), Line(-8, 5, -8, 10)];

        var list = new List<LibrarySymbol>
        {
            // ---- Kumanda
            Def("Kumanda", "Basma düğmesi (NO)", "S", "Basma düğmesi", With(NoContact, stem), VerticalPins("3", "4", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Kumanda", "Basma düğmesi (NC)", "S", "Basma düğmesi", With(NcContact, stem), VerticalPins("1", "2", new(PinKind.Signal, Contact: ContactType.NormallyClosed))),
            Def("Kumanda", "Acil stop (NC)", "S", "Acil stop düğmesi", With(NcContact, Line(-3, 7.5, -7, 7.5), Rect(-10, 4.5, 3, 6)), VerticalPins("11", "12", new(PinKind.Signal, Contact: ContactType.NormallyClosed))),
            Def("Kumanda", "Seçici anahtar (NO)", "S", "Seçici anahtar", With(NoContact, Line(-3, 7.5, -8, 7.5), Line(-8, 7.5, -8, 11)), VerticalPins("3", "4", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Kumanda", "Sınır anahtarı (NO)", "S", "Sınır anahtarı", With(NoContact, Line(-3, 7.5, -6, 7.5), Circle(-7.5, 7.5, 1.5)), VerticalPins("3", "4", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Kumanda", "Sınır anahtarı (NC)", "S", "Sınır anahtarı", With(NcContact, Line(0, 7.5, -4.5, 7.5), Circle(-6, 7.5, 1.5)), VerticalPins("1", "2", new(PinKind.Signal, Contact: ContactType.NormallyClosed))),
            Def("Kumanda", "Basınç şalteri (NO)", "B", "Basınç şalteri", With(NoContact, Line(-3, 7.5, -7, 7.5), Text(-9.5, 9, "p", 3)), VerticalPins("1", "2", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),

            // ---- Sinyal
            Def("Sinyal", "Sinyal lambası", "P", "Sinyal lambası", [Line(0, 0, 0, 3.5), Circle(0, 7.5, 4), Line(-2.8, 4.7, 2.8, 10.3), Line(-2.8, 10.3, 2.8, 4.7), Line(0, 11.5, 0, 15)], VerticalPins("X1", "X2")),
            Def("Sinyal", "Sirenli / buzzer", "H", "Buzzer", [Line(0, 0, 0, 3.5), Circle(0, 7.5, 4), Text(0, 9, "H", 3.5), Line(0, 11.5, 0, 15)], VerticalPins("1", "2")),

            // ---- Koruma
            Def("Koruma", "Sigorta", "F", "Sigorta", [Line(0, 0, 0, 15), Rect(-1.5, 4, 3, 7)], VerticalPins("1", "2", Power)),
            Def("Koruma", "Devre kesici (1 kutup)", "Q", "Devre kesici", With(NoContact, Line(-1.5, 3, 1.5, 6), Line(-1.5, 6, 1.5, 3)), VerticalPins("1", "2", Power)),
            Def("Koruma", "Motor koruma şalteri (1 kutup)", "Q", "Motor koruma şalteri", With(NoContact, Line(-1.5, 3, 1.5, 6), Line(-1.5, 6, 1.5, 3), Rect(2.5, 6, 4, 4)), VerticalPins("1", "2", Power)),
            Def("Koruma", "Termik röle", "F", "Termik röle", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Line(-3, 7.5, -1.5, 5.5), Line(-1.5, 5.5, 0, 9.5), Line(0, 9.5, 1.5, 5.5), Line(1.5, 5.5, 3, 7.5), Line(0, 10.5, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Koruma", "Aşırı gerilim koruyucu", "F", "Aşırı gerilim koruyucu", [Line(0, 0, 0, 4.5), Rect(-3, 4.5, 6, 6), Line(0, 5.5, 0, 9.5), Line(0, 9.5, -1, 8.2), Line(0, 9.5, 1, 8.2), Line(0, 10.5, 0, 15)], VerticalPins("1", "2", Power)),

            // ---- Röle ve kontaktör
            Def("Röle ve kontaktör", "Bobin", "K", "Kontaktör bobini", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Line(0, 10.5, 0, 15)], VerticalPins("A1", "A2", Power)),
            Def("Röle ve kontaktör", "Zaman rölesi bobini", "K", "Zaman rölesi", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Line(-4, 4.5, 4, 10.5), Line(0, 10.5, 0, 15)], VerticalPins("A1", "A2", Power)),
            Def("Röle ve kontaktör", "Güç kontağı (NO)", "K", "Kontaktör", With(NoContact, Line(0, 10, -3, 4.5, 0.5)), VerticalPins("1", "2", new(PinKind.Power, Contact: ContactType.NormallyOpen))),
            Def("Röle ve kontaktör", "Yardımcı kontak (NO)", "K", "Yardımcı kontak", NoContact, VerticalPins("13", "14", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Röle ve kontaktör", "Yardımcı kontak (NC)", "K", "Yardımcı kontak", NcContact, VerticalPins("21", "22", new(PinKind.Signal, Contact: ContactType.NormallyClosed))),
            Def("Röle ve kontaktör", "Zaman rölesi kontağı (NO, çekmede gecikmeli)", "K", "Zaman rölesi kontağı", With(NoContact, Circle(-5.5, 7.5, 1.2)), VerticalPins("17", "18", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Röle ve kontaktör", "Zaman rölesi kontağı (NC, çekmede gecikmeli)", "K", "Zaman rölesi kontağı", With(NcContact, Circle(-3.5, 7.5, 1.2)), VerticalPins("15", "16", new(PinKind.Signal, Contact: ContactType.NormallyClosed))),
            Def(
                "Röle ve kontaktör",
                "Değiştirmeli kontak (CO)",
                "K",
                "Değiştirmeli kontak",
                [Line(0, 0, 0, 5), Line(0, 5, 4.2, 11), Line(-5, 15, -5, 11), Line(-5, 11, -1, 11), Line(5, 15, 5, 11)],
                [
                    Pin(0, 0, 0, -1, "11", new(PinKind.Signal, Contact: ContactType.Changeover)),
                    Pin(-5, 15, 0, 1, "12", new(PinKind.Signal, Contact: ContactType.NormallyClosed)),
                    Pin(5, 15, 0, 1, "14", new(PinKind.Signal, Contact: ContactType.NormallyOpen)),
                ]),

            // ---- Motor ve aktüatör
            Def("Motor ve aktüatör", "Motor 1~", "M", "Tek fazlı motor", [Line(0, 0, 0, 7), Circle(0, 14, 7), Text(0, 15, "M", 4), Text(0, 18.5, "1~", 2.2), Line(0, 21, 0, 28)], [Pin(0, 0, 0, -1, "U1", Power), Pin(0, 28, 0, 1, "U2", Power)]),
            Def(
                "Motor ve aktüatör",
                "Motor 3~",
                "M",
                "Üç fazlı motor",
                [Line(0, 0, 0, 6), Line(0, 6, 5.2, 8.9), Line(10, 0, 10, 7), Line(20, 0, 20, 6), Line(20, 6, 14.8, 8.9), Circle(10, 14, 7), Text(10, 15, "M", 4), Text(10, 18.5, "3~", 2.2)],
                [Pin(0, 0, 0, -1, "U1", Power), Pin(10, 0, 0, -1, "V1", Power), Pin(20, 0, 0, -1, "W1", Power)]),
            Def("Motor ve aktüatör", "Selenoid valf bobini", "Y", "Selenoid valf", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Line(-4, 10.5, 4, 4.5), Line(0, 10.5, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Motor ve aktüatör", "Isıtıcı rezistans", "E", "Isıtıcı", [Line(0, 0, 0, 4), Rect(-3, 4, 6, 7), Line(-3, 7.5, 3, 7.5), Line(0, 11, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Motor ve aktüatör", "Direnç", "R", "Direnç", [Line(0, 0, 0, 15), Rect(-1.5, 4, 3, 7)], VerticalPins("1", "2")),

            // ---- Güç
            Def(
                "Güç",
                "Trafo (tek faz)",
                "T",
                "Trafo",
                [Line(0, 0, 0, 5), Line(0, 5, 1.5, 5), Line(10, 0, 10, 5), Line(10, 5, 8.5, 5), Circle(5, 5, 3.5), Circle(5, 10, 3.5), Line(0, 15, 0, 10), Line(0, 10, 1.5, 10), Line(10, 15, 10, 10), Line(10, 10, 8.5, 10)],
                [Pin(0, 0, 0, -1, "1U1", Power), Pin(10, 0, 0, -1, "1U2", Power), Pin(0, 15, 0, 1, "2U1", Power), Pin(10, 15, 0, 1, "2U2", Power)]),
            Def(
                "Güç",
                "Güç kaynağı AC/DC 24 V",
                "G",
                "Güç kaynağı",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Rect(-4, 3, 18, 9), Text(5, 8, "AC/DC", 2.6), Line(0, 12, 0, 15), Line(10, 12, 10, 15)],
                [Pin(0, 0, 0, -1, "L", Power), Pin(10, 0, 0, -1, "N", Power), Pin(0, 15, 0, 1, "24V", Power), Pin(10, 15, 0, 1, "0V", Power)]),

            // ---- Klemens ve konnektör
            Def("Klemens ve konnektör", "Klemens", "X", "Klemens", [Line(0, 0, 0, 6.5), Circle(0, 7.5, 1), Line(0, 8.5, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Klemens ve konnektör", "PE klemensi", "X", "PE klemensi", [Line(0, 0, 0, 6.5), Circle(0, 7.5, 1), Line(0, 8.5, 0, 15), Text(3, 9, "PE", 2.2, TextAlign.Left)], VerticalPins("PE.a", "PE.b", Earth)),
            Def(
                "Klemens ve konnektör",
                "M12 konnektör (4 pin)",
                "X",
                "M12 konnektör",
                RoundConnector("M12", 4, 9),
                [.. new[] { "1", "2", "3", "4" }.Select((name, i) => Pin(i * 5, 0, 0, -1, name, new(PinKind.Signal, ConnectorType.M12, ConnectorCode: "A-kod 4 pin")))]),
            Def(
                "Klemens ve konnektör",
                "M8 konnektör (3 pin)",
                "X",
                "M8 konnektör",
                RoundConnector("M8", 3, 6.5),
                [.. new[] { "1", "3", "4" }.Select((name, i) => Pin(i * 5, 0, 0, -1, name, new(PinKind.Signal, ConnectorType.M8, ConnectorCode: "3 pin")))]),
            Def(
                "Klemens ve konnektör",
                "Ethernet portu (RJ45)",
                "X",
                "Ethernet portu",
                [Line(0, 0, 0, 3), Rect(-5, 3, 10, 9), Text(0, 9, "RJ45", 2.4)],
                [Pin(0, 0, 0, -1, "ETH", new(PinKind.Communication, ConnectorType.Rj45, ConnectorCode: "Cat6"))]),

            // ---- Sensör
            Def("Sensör", "Endüktif sensör (NO, PNP)", "B", "Endüktif yakınlık sensörü", SensorBody("B"), SensorPins(ContactType.NormallyOpen)),
            Def("Sensör", "Endüktif sensör (NC, PNP)", "B", "Endüktif yakınlık sensörü", SensorBody("B"), SensorPins(ContactType.NormallyClosed)),
            Def("Sensör", "Fotoelektrik sensör (NO, PNP)", "B", "Fotoelektrik sensör", [.. SensorBody("B"), Line(2, 4.5, 8, 4.5)], SensorPins(ContactType.NormallyOpen)),
            Def("Sensör", "Kapasitif sensör (NO, PNP)", "B", "Kapasitif sensör", [.. SensorBody("B"), Line(2, 10.5, 8, 10.5)], SensorPins(ContactType.NormallyOpen)),

            // ---- Otomasyon
            Def(
                "Otomasyon",
                "Ethernet anahtarı (5 port)",
                "A",
                "Ethernet anahtarı",
                [.. Enumerable.Range(0, 5).Select(i => Line(i * 5, 0, i * 5, 3)), Rect(-3, 3, 31, 9), Text(12.5, 9, "SWITCH", 2.8), Line(0, 12, 0, 15), Line(5, 12, 5, 15)],
                [
                    .. Enumerable.Range(1, 5).Select(i => Pin((i - 1) * 5, 0, 0, -1, $"P{i}", new(PinKind.Communication, ConnectorType.Rj45))),
                    Pin(0, 15, 0, 1, "24V", Power),
                    Pin(5, 15, 0, 1, "0V", Power),
                ]),
            Def(
                "Otomasyon",
                "Frekans konvertörü",
                "A",
                "Frekans konvertörü",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Line(20, 0, 20, 3), Rect(-3, 3, 26, 14), Text(10, 11, "VFD", 3.2), Line(0, 17, 0, 20), Line(10, 17, 10, 20), Line(20, 17, 20, 20)],
                [
                    Pin(0, 0, 0, -1, "L1", Power),
                    Pin(10, 0, 0, -1, "L2", Power),
                    Pin(20, 0, 0, -1, "L3", Power),
                    Pin(0, 20, 0, 1, "U", Power),
                    Pin(10, 20, 0, 1, "V", Power),
                    Pin(20, 20, 0, 1, "W", Power),
                ]),
            Def(
                "Otomasyon",
                "Servo sürücü",
                "A",
                "Servo sürücü",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Line(20, 0, 20, 3), Rect(-3, 3, 26, 14), Text(10, 11, "SERVO", 3), Line(0, 17, 0, 20), Line(10, 17, 10, 20), Line(20, 17, 20, 20)],
                [
                    Pin(0, 0, 0, -1, "L1", Power),
                    Pin(10, 0, 0, -1, "L2", Power),
                    Pin(20, 0, 0, -1, "L3", Power),
                    Pin(0, 20, 0, 1, "U", Power),
                    Pin(10, 20, 0, 1, "V", Power),
                    Pin(20, 20, 0, 1, "W", Power),
                ]),
        };

        list.AddRange(BuildExtended());
        list.AddRange(BuildPneumatic());
        return list;
    }

    // M8/M12 konnektör yuvarlaktır: daire ve uçlara inen kısa çizgiler (uç adımı 5 mm, uçlar üst kenarda).
    private static SymbolShape[] RoundConnector(string label, int pins, double radius)
    {
        var centerX = (pins - 1) * 5 / 2.0;
        var centerY = radius + 1.5;
        var shapes = new List<SymbolShape> { Circle(centerX, centerY, radius), Text(centerX, centerY + 1.2, label, 2.8) };
        for (var i = 0; i < pins; i++)
        {
            var x = i * 5.0;
            var edge = centerY - Math.Sqrt((radius * radius) - ((x - centerX) * (x - centerX)));
            shapes.Add(Line(x, 0, x, Math.Round(edge, 2)));
        }

        return [.. shapes];
    }

    // Üç telli sensör: kahverengi (+), mavi (-), siyah (çıkış).
    private static SymbolShape[] SensorBody(string letter) =>
    [
        Line(0, 0, 0, 3), Line(5, 0, 5, 3), Line(10, 0, 10, 3), Rect(-3, 3, 16, 9), Text(5, 9, letter, 3.2),
    ];

    private static SymbolPin[] SensorPins(ContactType output) =>
    [
        Pin(0, 0, 0, -1, "BN", new(PinKind.Power, ConnectorType.M8)),
        Pin(5, 0, 0, -1, "BU", new(PinKind.Power, ConnectorType.M8)),
        Pin(10, 0, 0, -1, "BK", new(PinKind.Signal, ConnectorType.M8, output)),
    ];
}
