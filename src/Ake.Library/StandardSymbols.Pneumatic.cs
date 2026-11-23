using Ake.Core.Model;

namespace Ake.Library;

/// <summary>
/// Pnömatik standart semboller (ADR-0052): hazırlama grubu, valfler ve silindirler. ISO 1219-1'in sadeleştirilmiş, bu projeye özgü elle çizilmiş
/// biçimidir. Uç adları ISO 5599 / ISO 1219'a uyar: 1 basınç, 2 ve 4 çalışma, 3 ve 5 egzoz, A/B silindir portları. Elektrik tarafı (selenoid bobini)
/// ayrı bir fonksiyondur (Motor ve aktüatör › Selenoid valf bobini); bir valf cihazı hem pnömatik gövdeyi hem bobinleri taşır.
/// </summary>
public static partial class StandardSymbols
{
    private const string PneumaticCategory = "Pnömatik";

    private static readonly PointAttributes PortThread = new(PinKind.Pneumatic, ConnectorType.Thread, ConnectorCode: "G1/4");
    private static readonly PointAttributes PortPushIn = new(PinKind.Pneumatic, ConnectorType.PushIn, ConnectorCode: "Ø6 mm");

    // Ok: çizgi ve iki kanatlı uç (akış yönü).
    private static SymbolShape[] Arrow(double x1, double y1, double x2, double y2)
    {
        const double head = 1.8;
        var angle = Math.Atan2(y2 - y1, x2 - x1);
        double X(double a) => Math.Round(x2 - (head * Math.Cos(angle + a)), 2);
        double Y(double a) => Math.Round(y2 - (head * Math.Sin(angle + a)), 2);
        return [Line(x1, y1, x2, y2), Line(x2, y2, X(0.45), Y(0.45)), Line(x2, y2, X(-0.45), Y(-0.45))];
    }

    // Kapalı yol: akışın kesildiği uç ("T").
    private static SymbolShape[] Block(double x, double bottom) =>
        [Line(x, bottom, x, bottom - 3), Line(x - 1.3, bottom - 3, x + 1.3, bottom - 3)];

    // Yay: yatay zikzak (valf ve regülatör için); uçlarda düz kısımlar, ortada altı tepeli dalga.
    private static SymbolShape[] SpringH(double x, double y, double length)
    {
        var points = new List<(double X, double Y)> { (x, y), (x + (length * 0.1), y) };
        for (var k = 0; k < 6; k++)
        {
            points.Add((Math.Round(x + (length * 0.1) + (length * 0.8 * (k + 0.5) / 6), 2), k % 2 == 0 ? y - 2 : y + 2));
        }

        points.Add((x + (length * 0.9), y));
        points.Add((x + length, y));
        return [.. points.Zip(points.Skip(1), (a, b) => Line(a.X, a.Y, b.X, b.Y))];
    }

    // Selenoid tahriki: kare ve köşegen.
    private static SymbolShape[] SolenoidDrive(double x) => [Rect(x, 7, 6, 6), Line(x, 13, x + 6, 7)];

    // Elmas: hazırlama grubu ve filtre gövdesi (üst ve alt uç 5 mm'lik bağlantı çizgisiyle).
    private static SymbolShape[] Diamond() =>
        [Line(0, 0, 0, 5), Line(0, 5, 5, 10), Line(5, 10, 0, 15), Line(0, 15, -5, 10), Line(-5, 10, 0, 5), Line(0, 15, 0, 20)];

    private static LibrarySymbol Pneu(string name, string tag, string deviceName, SymbolShape[] shapes, SymbolPin[] pins) =>
        Def(PneumaticCategory, name, tag, deviceName, shapes, pins);

    private static IEnumerable<LibrarySymbol> BuildPneumatic()
    {
        SymbolPin[] inOut(PointAttributes attributes) =>
            [Pin(0, 0, 0, -1, "1", attributes), Pin(0, 20, 0, 1, "2", attributes)];

        // 3/2 valf (selenoid, yay): iki kare 12×10; bağlantılar dinlenme (sağ, yaylı) karesinde.
        SymbolShape[] valve32 =
        [
            Rect(0, 5, 12, 10), Rect(12, 5, 12, 10),
            .. SolenoidDrive(-6), .. SpringH(24, 10, 6),
            .. Arrow(3, 14, 6, 6), .. Block(9, 15),
            .. Block(15, 15), .. Arrow(18, 6, 21, 14),
            Line(18, 0, 18, 5), Line(15, 15, 15, 20), Line(21, 15, 21, 20),
        ];
        SymbolPin[] valve32Pins =
        [
            Pin(18, 0, 0, -1, "2", PortPushIn),
            Pin(15, 20, 0, 1, "1", PortPushIn),
            Pin(21, 20, 0, 1, "3", PortPushIn),
        ];

        // 5/2 valf: iki kare 18×10; bağlantılar dinlenme karesinde (sağ). Üstte 4 ve 2, altta 5, 1, 3.
        SymbolShape[] body52 =
        [
            Rect(0, 5, 18, 10), Rect(18, 5, 18, 10),
            .. Arrow(9, 14, 4.5, 6), .. Arrow(13.5, 6, 15, 14),
            .. Arrow(27, 14, 31.5, 6), .. Arrow(22.5, 6, 21, 14),
            Line(22.5, 0, 22.5, 5), Line(31.5, 0, 31.5, 5),
            Line(21, 15, 21, 20), Line(27, 15, 27, 20), Line(33, 15, 33, 20),
        ];
        SymbolPin[] pins52 =
        [
            Pin(22.5, 0, 0, -1, "4", PortPushIn),
            Pin(31.5, 0, 0, -1, "2", PortPushIn),
            Pin(21, 20, 0, 1, "5", PortPushIn),
            Pin(27, 20, 0, 1, "1", PortPushIn),
            Pin(33, 20, 0, 1, "3", PortPushIn),
        ];

        // Silindir gövdesi: üstte A (kapak tarafı) portu, solda B (mil tarafı) portu; piston ve mil.
        SymbolShape[] cylinderBody =
        [
            Line(0, 0, 0, 5), Rect(-5, 5, 10, 20), Line(-5, 12, 5, 12), Line(0, 12, 0, 32), Line(-2.5, 32, 2.5, 32),
        ];

        return
        [
            Pneu(
                "Kompresör", "M", "Kompresör",
                [Line(0, 0, 0, 4), Circle(0, 10, 6), Line(-4.5, 13, 0, 6), Line(0, 6, 4.5, 13), Line(-4.5, 13, 4.5, 13)],
                [Pin(0, 0, 0, -1, "1", PortThread)]),
            Pneu(
                "Hava deposu", "C", "Hava deposu",
                [Line(0, 0, 0, 5), Rect(-5, 5, 10, 16), Line(0, 21, 0, 26)],
                [Pin(0, 0, 0, -1, "1", PortThread), Pin(0, 26, 0, 1, "2", PortThread)]),
            Pneu(
                "Hava hazırlama ünitesi (FRL)", "F", "Hava hazırlama ünitesi",
                [.. Diamond(), Line(-2.5, 12.5, 2.5, 7.5), Line(-2.5, 7.5, 2.5, 12.5)],
                inOut(PortThread)),
            Pneu(
                "Filtre", "F", "Hava filtresi",
                [.. Diamond(), Line(0, 6.5, 0, 8), Line(0, 9.2, 0, 10.8), Line(0, 12, 0, 13.5)],
                inOut(PortThread)),
            Pneu(
                "Basınç regülatörü", "Q", "Basınç regülatörü",
                [Line(0, 0, 0, 5), Rect(-5, 5, 10, 10), .. Arrow(0, 13, 0, 7), .. SpringH(5, 10, 6), Line(0, 15, 0, 20)],
                inOut(PortThread)),
            Pneu(
                "Manometre", "B", "Manometre",
                [Line(0, 0, 0, 6), Circle(0, 12, 6), .. Arrow(0, 12, 3.5, 8.5)],
                [Pin(0, 0, 0, -1, "1", PortThread)]),
            Pneu(
                "Susturucu (egzoz)", "R", "Susturucu",
                [Line(0, 0, 0, 5), Line(0, 5, -3.5, 10), Line(-3.5, 10, 3.5, 10), Line(3.5, 10, 0, 5)],
                [Pin(0, 0, 0, -1, "3", PortPushIn)]),
            Pneu(
                "Çek valf", "Q", "Çek valf",
                [Line(0, 0, 0, 5), Circle(0, 8, 2.2), Line(-3.5, 13, -1.2, 10.3), Line(3.5, 13, 1.2, 10.3), Line(0, 15, 0, 20)],
                [Pin(0, 0, 0, -1, "2", PortPushIn), Pin(0, 20, 0, 1, "1", PortPushIn)]),
            Pneu(
                "Tek yönlü hız kontrol valfi", "Q", "Hız kontrol valfi",
                [Line(0, 0, 0, 5), Rect(-5, 5, 10, 10), .. Arrow(-3.5, 13.5, 3.5, 6.5), Circle(0, 10, 1), Line(0, 15, 0, 20)],
                inOut(PortPushIn)),
            Pneu("3/2 valf, selenoid / yay (NC)", "Q", "3/2 valf", valve32, valve32Pins),
            Pneu("5/2 valf, selenoid / yay", "Q", "5/2 valf", [.. body52, .. SolenoidDrive(-6), .. SpringH(36, 10, 6)], pins52),
            Pneu("5/2 valf, çift selenoid (hafızalı)", "Q", "5/2 valf", [.. body52, .. SolenoidDrive(-6), .. SolenoidDrive(36)], pins52),
            Pneu(
                "Silindir (çift etkili)", "M", "Çift etkili silindir",
                [.. cylinderBody, Line(-5, 22, -9, 22)],
                [Pin(0, 0, 0, -1, "A", PortPushIn), Pin(-9, 22, -1, 0, "B", PortPushIn)]),
            Pneu(
                "Silindir (tek etkili, yaylı)", "M", "Tek etkili silindir",
                [.. cylinderBody, Line(-3, 14, 3, 15.5), Line(3, 15.5, -3, 17.5), Line(-3, 17.5, 3, 19.5), Line(3, 19.5, -3, 21.5), Line(-5, 22, -7, 22)],
                [Pin(0, 0, 0, -1, "A", PortPushIn)]),
        ];
    }
}
