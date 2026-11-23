using Ake.Core.Model;

namespace Ake.Library;

/// <summary>
/// Genişletilmiş IEC 60617 standart sembol seti: kumanda, ölçüm/sinyal, çok kutuplu koruma,
/// röle ve kontaktör çeşitleri, motor ve güç elektroniği, klemens/konnektör ve endüstriyel sensörler.
/// </summary>
public static partial class StandardSymbols
{
    private static IEnumerable<LibrarySymbol> BuildExtended()
    {
        SymbolShape[] mushroomHead = [Line(-3, 7.5, -7, 7.5), Line(-7, 4, -7, 11), Circle(-7, 7.5, 2.5)];
        SymbolShape[] keySwitchHead = [Line(-3, 7.5, -7, 7.5), Rect(-10.5, 5.5, 3.5, 4), Line(-10.5, 7.5, -12, 7.5)];
        SymbolShape[] pedalHead = [Line(-3, 7.5, -7, 7.5), Line(-7, 5, -10.5, 7.5), Line(-10.5, 7.5, -7, 10)];

        return
        [
            // ---- Kumanda (9)
            Def("Kumanda", "Mantar buton (NO)", "S", "Mantar buton", With(NoContact, mushroomHead), VerticalPins("3", "4", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Kumanda", "Mantar buton (NC)", "S", "Mantar buton", With(NcContact, mushroomHead), VerticalPins("1", "2", new(PinKind.Signal, Contact: ContactType.NormallyClosed))),
            Def(
                "Kumanda",
                "Işıklı basma düğmesi (NO)",
                "S",
                "Işıklı buton",
                [.. NoContact, Line(-3, 7.5, -7, 7.5), Line(-7, 5, -7, 10), Circle(-10.5, 7.5, 2), Line(-11.8, 6.2, -9.2, 8.8), Line(-11.8, 8.8, -9.2, 6.2)],
                [Pin(0, 0, 0, -1, "3", new(PinKind.Signal, Contact: ContactType.NormallyOpen)), Pin(0, 15, 0, 1, "4", new(PinKind.Signal, Contact: ContactType.NormallyOpen)), Pin(-10.5, 0, 0, -1, "X1", Signal), Pin(-10.5, 15, 0, 1, "X2", Signal)]),
            Def("Kumanda", "Anahtarlı seçici şalter (NO)", "S", "Anahtarlı şalter", With(NoContact, keySwitchHead), VerticalPins("3", "4", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def(
                "Kumanda",
                "Seçici anahtar 1-0-2 (CO)",
                "S",
                "Seçici anahtar",
                [Line(0, 0, 0, 5), Line(0, 5, 4.2, 11), Line(-5, 15, -5, 11), Line(-5, 11, -1, 11), Line(5, 15, 5, 11), Line(-4, 7.5, -8, 7.5), Line(-8, 4.5, -8, 10.5), Text(-10, 6, "1", 2), Text(-10, 11, "2", 2)],
                [Pin(0, 0, 0, -1, "11", new(PinKind.Signal, Contact: ContactType.Changeover)), Pin(-5, 15, 0, 1, "12", new(PinKind.Signal, Contact: ContactType.NormallyClosed)), Pin(5, 15, 0, 1, "14", new(PinKind.Signal, Contact: ContactType.NormallyOpen))]),
            Def("Kumanda", "Ayak pedalı (NO)", "S", "Ayak pedalı", With(NoContact, pedalHead), VerticalPins("3", "4", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Kumanda", "Termostat / sıcaklık şalteri (NO)", "B", "Termostat", With(NoContact, Line(-3, 7.5, -6, 7.5), Text(-9, 8.8, "θ", 3.2)), VerticalPins("1", "2", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Kumanda", "Seviye şalteri / şamandıra (NO)", "B", "Seviye şalteri", With(NoContact, Line(-3, 7.5, -6.5, 7.5), Circle(-8.5, 7.5, 2), Line(-10.5, 7.5, -6.5, 7.5)), VerticalPins("1", "2", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Kumanda", "Akış şalteri (NO)", "B", "Akış şalteri", With(NoContact, Line(-3, 7.5, -6.5, 7.5), Rect(-10.5, 5.5, 4, 4), Line(-9.5, 9.5, -7.5, 5.5)), VerticalPins("1", "2", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),

            // ---- Sinyal (6)
            Def("Sinyal", "Flaşörlü ikaz lambası (beacon)", "H", "İkaz tepe lambası", [Line(0, 0, 0, 3), Circle(0, 7.5, 4.5), Line(-3.2, 4.3, 3.2, 10.7), Line(-3.2, 10.7, 3.2, 4.3), Line(-5.5, 5, -7, 4), Line(5.5, 5, 7, 4), Line(0, 12, 0, 15)], VerticalPins("X1", "X2")),
            Def("Sinyal", "Korna / klakson", "H", "Korna", [Line(0, 0, 0, 4), Rect(-3, 4, 6, 7), Line(3, 5, 6.5, 3), Line(3, 10, 6.5, 12), Line(6.5, 3, 6.5, 12), Line(0, 11, 0, 15)], VerticalPins("1", "2")),
            Def("Sinyal", "Voltmetre", "P", "Voltmetre", [Line(0, 0, 0, 3.5), Circle(0, 7.5, 4), Text(0, 8.7, "V", 3.5), Line(0, 11.5, 0, 15)], VerticalPins("1", "2")),
            Def("Sinyal", "Ampermetre", "P", "Ampermetre", [Line(0, 0, 0, 3.5), Circle(0, 7.5, 4), Text(0, 8.7, "A", 3.5), Line(0, 11.5, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Sinyal", "Çalışma saati sayacı", "P", "Sayaç", [Line(0, 0, 0, 4), Rect(-4.5, 4, 9, 7), Text(0, 8.7, "h", 3.2), Line(0, 11, 0, 15)], VerticalPins("1", "2")),
            Def("Sinyal", "LED gösterge", "P", "LED gösterge", [Line(0, 0, 0, 5), Line(-3, 5, 3, 5), Line(-3, 5, 0, 10), Line(3, 5, 0, 10), Line(-3, 10, 3, 10), Line(0, 10, 0, 15)], VerticalPins("+", "-")),

            // ---- Koruma (7)
            Def("Koruma", "Devre kesici (3 kutup)", "Q", "Devre kesici 3P", ThreePoleBreakerShapes(isMotorBreaker: false), ThreePolePowerPins("1", "2", "3", "4", "5", "6")),
            Def("Koruma", "Motor koruma şalteri (3 kutup)", "Q", "Motor koruma şalteri 3P", ThreePoleBreakerShapes(isMotorBreaker: true), ThreePolePowerPins("1", "2", "3", "4", "5", "6")),
            Def("Koruma", "Termik röle (3 kutup)", "F", "Termik röle 3P", ThreePoleThermalShapes(), ThreePolePowerPins("1", "2", "3", "4", "5", "6")),
            Def("Koruma", "Yük ayırıcı / ana şalter (1 kutup)", "Q", "Yük ayırıcı", With(NoContact, Line(-1.8, 4.5, 1.8, 4.5), Line(-4, 7.5, -1.5, 7.5)), VerticalPins("1", "2", Power)),
            Def("Koruma", "Yük ayırıcı / ana şalter (3 kutup)", "Q", "Ana şalter 3P", ThreePoleSwitchDisconnectorShapes(), ThreePolePowerPins("1", "2", "3", "4", "5", "6")),
            Def(
                "Koruma",
                "Kaçak akım koruma şalteri (RCD 2P)",
                "Q",
                "Kaçak akım rölesi",
                [.. MultiPoleContactShapes(2), Line(3.5, 11.5, 6.5, 11.5), Circle(5, 11.5, 1.8), Line(-1.5, 3, 1.5, 6), Line(-1.5, 6, 1.5, 3), Line(8.5, 3, 11.5, 6), Line(8.5, 6, 11.5, 3), Line(0, 7.5, 10, 7.5)],
                [Pin(0, 0, 0, -1, "1", Power), Pin(0, 15, 0, 1, "2", Power), Pin(10, 0, 0, -1, "N1", Power), Pin(10, 15, 0, 1, "N2", Power)]),
            Def("Koruma", "Varistör (MOV)", "F", "Varistör", [Line(0, 0, 0, 15), Rect(-2, 4.5, 4, 6), Line(-3.5, 11, 2.5, 4), Line(-3.5, 11, -3.5, 12.5)], VerticalPins("1", "2")),

            // ---- Röle ve kontaktör (8)
            Def("Röle ve kontaktör", "Güç kontağı (3 kutup NO)", "K", "Kontaktör güç kontağı 3P", [.. MultiPoleContactShapes(3), Line(-1.5, 7.5, 20, 7.5)], ThreePolePowerPins("1", "2", "3", "4", "5", "6")),
            Def("Röle ve kontaktör", "Güç kontağı (NC)", "K", "Kontaktör güç kontağı (NC)", With(NcContact, Line(0, 10, 2.6, 4.2, 0.5)), VerticalPins("1", "2", new(PinKind.Power, Contact: ContactType.NormallyClosed))),
            Def("Röle ve kontaktör", "Zaman rölesi bobini (düşmede gecikmeli)", "K", "Zaman rölesi (TOF)", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Rect(-4, 4.5, 2.5, 6), Line(-2.7, 4.5, -2.7, 10.5), Line(0, 10.5, 0, 15)], VerticalPins("A1", "A2", Power)),
            Def("Röle ve kontaktör", "Flaşör röle bobini", "K", "Flaşör röle", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Line(-3, 7.5, -1, 7.5), Line(-1, 7.5, -1, 5.8), Line(-1, 5.8, 1, 5.8), Line(1, 5.8, 1, 9.2), Line(1, 9.2, 3, 9.2), Line(0, 10.5, 0, 15)], VerticalPins("A1", "A2", Power)),
            Def("Röle ve kontaktör", "Kilitlemeli (remanans) röle bobini", "K", "Darbe / kilitlemeli röle", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Line(-4, 7.5, -1.5, 7.5), Line(-1.5, 4.5, -1.5, 10.5), Line(0, 10.5, 0, 15)], VerticalPins("A1", "A2", Power)),
            Def("Röle ve kontaktör", "Zaman rölesi kontağı (NO, düşmede gecikmeli)", "K", "Zaman rölesi kontağı", [.. NoContact, Line(-3, 7.5, -5.5, 7.5), Circle(-6.8, 7.5, 1.3), Line(-6.8, 6.2, -6.8, 8.8)], VerticalPins("57", "58", new(PinKind.Signal, Contact: ContactType.NormallyOpen))),
            Def("Röle ve kontaktör", "Zaman rölesi kontağı (NC, düşmede gecikmeli)", "K", "Zaman rölesi kontağı", [.. NcContact, Line(0, 7.5, -3.5, 7.5), Circle(-4.8, 7.5, 1.3), Line(-4.8, 6.2, -4.8, 8.8)], VerticalPins("55", "56", new(PinKind.Signal, Contact: ContactType.NormallyClosed))),
            Def(
                "Röle ve kontaktör",
                "Emniyet rölesi modülü",
                "K",
                "Emniyet rölesi",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Line(20, 0, 20, 3), Rect(-3, 3, 26, 14), Text(10, 11, "SAFE", 3), Line(0, 17, 0, 20), Line(10, 17, 10, 20), Line(20, 17, 20, 20)],
                [Pin(0, 0, 0, -1, "A1", Power), Pin(10, 0, 0, -1, "S11", Signal), Pin(20, 0, 0, -1, "S21", Signal), Pin(0, 20, 0, 1, "A2", Power), Pin(10, 20, 0, 1, "13", Signal), Pin(20, 20, 0, 1, "14", Signal)]),

            // ---- Motor ve aktüatör (7)
            Def("Motor ve aktüatör", "DC motor", "M", "DC motor", [Line(0, 0, 0, 7), Circle(0, 14, 7), Text(0, 15, "M", 4), Line(-3, 18, 3, 18), Line(0, 21, 0, 28)], [Pin(0, 0, 0, -1, "+", Power), Pin(0, 28, 0, 1, "-", Power)]),
            Def(
                "Motor ve aktüatör",
                "Motor 3~ + PE",
                "M",
                "Üç fazlı motor (PE)",
                [Line(0, 0, 0, 6), Line(0, 6, 5.2, 8.9), Line(10, 0, 10, 7), Line(20, 0, 20, 6), Line(20, 6, 14.8, 8.9), Line(30, 0, 30, 14), Line(30, 14, 17, 14), Circle(10, 14, 7), Text(10, 15, "M", 4), Text(10, 18.5, "3~", 2.2)],
                [Pin(0, 0, 0, -1, "U1", Power), Pin(10, 0, 0, -1, "V1", Power), Pin(20, 0, 0, -1, "W1", Power), Pin(30, 0, 0, -1, "PE", Earth)]),
            Def(
                "Motor ve aktüatör",
                "Step motor",
                "M",
                "Step motor",
                [Line(0, 0, 0, 6), Line(5, 0, 5, 7), Line(15, 0, 15, 7), Line(20, 0, 20, 6), Circle(10, 14, 7), Text(10, 15, "M", 4), Text(10, 18.5, "STEP", 2)],
                [Pin(0, 0, 0, -1, "A+", Power), Pin(5, 0, 0, -1, "A-", Power), Pin(15, 0, 0, -1, "B+", Power), Pin(20, 0, 0, -1, "B-", Power)]),
            Def(
                "Motor ve aktüatör",
                "Servo motor 3~",
                "M",
                "Servo motor",
                [Line(0, 0, 0, 6), Line(10, 0, 10, 7), Line(20, 0, 20, 6), Circle(10, 14, 7), Text(10, 15, "M", 4), Text(10, 18.5, "SRV", 2), Line(10, 21, 10, 24), Rect(5, 24, 10, 4)],
                [Pin(0, 0, 0, -1, "U", Power), Pin(10, 0, 0, -1, "V", Power), Pin(20, 0, 0, -1, "W", Power), Pin(10, 28, 0, 1, "ENC", Signal)]),
            Def("Motor ve aktüatör", "Elektromanyetik fren bobini", "Y", "Fren bobini", [Line(0, 0, 0, 4.5), Rect(-4, 4.5, 8, 6), Line(-4, 4.5, 0, 7.5), Line(4, 4.5, 0, 7.5), Line(0, 10.5, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Motor ve aktüatör", "Fan / vantilatör motoru", "M", "Pano fanı", [Line(0, 0, 0, 4), Circle(0, 10, 6), Line(-3.5, 7.5, 3.5, 12.5), Line(-3.5, 12.5, 3.5, 7.5), Text(0, 14.5, "M", 2.4), Line(0, 16, 0, 20)], VerticalPins("L", "N", Power)),
            Def("Motor ve aktüatör", "Potansiyometre", "R", "Potansiyometre", [Line(0, 0, 0, 4), Rect(-1.8, 4, 3.6, 7), Line(0, 11, 0, 15), Line(5, 7.5, 1.8, 7.5), Line(3.2, 6.2, 1.8, 7.5), Line(3.2, 8.8, 1.8, 7.5)], [Pin(0, 0, 0, -1, "1", Signal), Pin(0, 15, 0, 1, "3", Signal), Pin(5, 7.5, 1, 0, "2", Signal)]),

            // ---- Güç (7)
            Def(
                "Güç",
                "Trafo (3 faz)",
                "T",
                "Üç fazlı trafo",
                [Line(0, 0, 0, 4), Line(10, 0, 10, 4), Line(20, 0, 20, 4), Circle(0, 7.5, 3.5), Circle(10, 7.5, 3.5), Circle(20, 7.5, 3.5), Circle(0, 12.5, 3.5), Circle(10, 12.5, 3.5), Circle(20, 12.5, 3.5), Line(0, 16, 0, 20), Line(10, 16, 10, 20), Line(20, 16, 20, 20)],
                ThreePolePowerPins("1U1", "2U1", "1V1", "2V1", "1W1", "2W1", height: 20)),
            Def("Güç", "Akım trafosu (CT)", "T", "Akım trafosu", [Line(0, 0, 0, 15), Circle(0, 7.5, 3.5), Line(3.5, 7.5, 7, 7.5), Line(7, 7.5, 7, 15)], [Pin(0, 0, 0, -1, "P1", Power), Pin(0, 15, 0, 1, "P2", Power), Pin(7, 15, 0, 1, "S1", Signal), Pin(7, 0, 0, -1, "S2", Signal)]),
            Def(
                "Güç",
                "Köprü doğrultucu",
                "V",
                "Köprü doğrultucu",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Rect(-3, 3, 16, 9), Line(1, 5, 9, 10), Line(9, 5, 1, 10), Line(0, 12, 0, 15), Line(10, 12, 10, 15)],
                [Pin(0, 0, 0, -1, "AC1", Power), Pin(10, 0, 0, -1, "AC2", Power), Pin(0, 15, 0, 1, "+", Power), Pin(10, 15, 0, 1, "-", Power)]),
            Def(
                "Güç",
                "DC/DC konvertör",
                "G",
                "DC/DC konvertör",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Rect(-4, 3, 18, 9), Text(5, 8.5, "DC/DC", 2.4), Line(0, 12, 0, 15), Line(10, 12, 10, 15)],
                [Pin(0, 0, 0, -1, "IN+", Power), Pin(10, 0, 0, -1, "IN-", Power), Pin(0, 15, 0, 1, "OUT+", Power), Pin(10, 15, 0, 1, "OUT-", Power)]),
            Def("Güç", "Akü / DC batarya", "G", "Batarya / akü", [Line(0, 0, 0, 6), Line(-4, 6, 4, 6), Line(-2, 9, 2, 9), Line(0, 9, 0, 15)], VerticalPins("+", "-", Power)),
            Def("Güç", "Kondansatör", "C", "Kondansatör", [Line(0, 0, 0, 6.2), Line(-4, 6.2, 4, 6.2), Line(-4, 8.8, 4, 8.8), Line(0, 8.8, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Güç", "Şok bobini / endüktans", "L", "Şok bobini", [Line(0, 0, 0, 4), Rect(-2.5, 4, 5, 7), Line(-2.5, 4, 2.5, 4), Line(0, 11, 0, 15)], VerticalPins("1", "2", Power)),

            // ---- Klemens ve konnektör (7)
            Def("Klemens ve konnektör", "Nötr (N) klemensi", "X", "Nötr klemensi", [Line(0, 0, 0, 6.5), Circle(0, 7.5, 1), Line(0, 8.5, 0, 15), Text(3, 9, "N", 2.4, TextAlign.Left)], VerticalPins("N.a", "N.b", Power)),
            Def("Klemens ve konnektör", "Sigortalı klemens", "X", "Sigortalı klemens", [Line(0, 0, 0, 5), Rect(-1.5, 5, 3, 5), Line(0, 5, 0, 10), Line(0, 10, 0, 15)], VerticalPins("1", "2", Power)),
            Def("Klemens ve konnektör", "Ayırma (bıçaklı) klemensi", "X", "Ayırma klemensi", [Line(0, 0, 0, 5.5), Circle(0, 5.5, 0.8), Line(0, 9.5, -2.5, 6), Line(0, 9.5, 0, 15)], VerticalPins("1", "2", Signal)),
            Def("Klemens ve konnektör", "Ekran (blendaj) klemensi", "X", "Blendaj klemensi", [Line(0, 0, 0, 6.5), Circle(0, 7.5, 1.2), Line(-3, 7.5, 3, 7.5), Line(0, 8.5, 0, 15)], VerticalPins("SH.a", "SH.b", Earth)),
            Def(
                "Klemens ve konnektör",
                "M12 konnektör (5 pin)",
                "X",
                "M12 konnektör",
                RoundConnector("M12", 5, 11.5),
                [.. new[] { "1", "2", "3", "4", "5" }.Select((name, i) => Pin(i * 5, 0, 0, -1, name, new(PinKind.Signal, ConnectorType.M12, ConnectorCode: "A-kod 5 pin")))]),
            Def(
                "Klemens ve konnektör",
                "M12 konnektör (8 pin)",
                "X",
                "M12 konnektör",
                RoundConnector("M12-8", 8, 18.5),
                [.. new[] { "1", "2", "3", "4", "5", "6", "7", "8" }.Select((name, i) => Pin(i * 5, 0, 0, -1, name, new(PinKind.Signal, ConnectorType.M12, ConnectorCode: "A-kod 8 pin")))]),
            Def(
                "Klemens ve konnektör",
                "D-Sub 9 pin konnektör",
                "X",
                "D-Sub 9 konnektör",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Line(20, 0, 20, 3), Rect(-3, 3, 26, 9), Text(10, 8.8, "DB9", 2.6)],
                [Pin(0, 0, 0, -1, "2", new(PinKind.Communication, ConnectorType.DSub, ConnectorCode: "DE-9")), Pin(10, 0, 0, -1, "3", new(PinKind.Communication, ConnectorType.DSub, ConnectorCode: "DE-9")), Pin(20, 0, 0, -1, "5", new(PinKind.Communication, ConnectorType.DSub, ConnectorCode: "DE-9"))]),

            // ---- Sensör (6)
            Def("Sensör", "Endüktif sensör (NO, NPN)", "B", "Endüktif yakınlık sensörü (NPN)", [.. SensorBody("B"), Text(5, 11.5, "NPN", 1.8)], SensorPins(ContactType.NormallyOpen)),
            Def("Sensör", "Fotoelektrik sensör (NC, PNP)", "B", "Fotoelektrik sensör", [.. SensorBody("B"), Line(2, 4.5, 8, 4.5), Line(2, 10.5, 8, 10.5)], SensorPins(ContactType.NormallyClosed)),
            Def("Sensör", "Ultrasonik mesafe sensörü (analog)", "B", "Ultrasonik sensör", [.. SensorBody("US"), Line(10, 10.5, 13, 10.5)], [Pin(0, 0, 0, -1, "BN", new(PinKind.Power, ConnectorType.M12)), Pin(5, 0, 0, -1, "BU", new(PinKind.Power, ConnectorType.M12)), Pin(10, 0, 0, -1, "AI", new(PinKind.Signal, ConnectorType.M12))]),
            Def("Sensör", "PT100 sıcaklık sensörü (3 telli)", "B", "PT100 sensör", [Line(0, 0, 0, 3), Line(5, 0, 5, 3), Line(10, 0, 10, 3), Rect(-3, 3, 16, 9), Text(5, 8.8, "Pt100", 2.4)], [Pin(0, 0, 0, -1, "1", Signal), Pin(5, 0, 0, -1, "2", Signal), Pin(10, 0, 0, -1, "3", Signal)]),
            Def("Sensör", "Termokupl sıcaklık sensörü", "B", "Termokupl", [Line(0, 0, 0, 4), Line(10, 0, 10, 4), Rect(-2, 4, 14, 8), Text(5, 9, "TC", 2.8)], [Pin(0, 0, 0, -1, "+", Signal), Pin(10, 0, 0, -1, "-", Signal)]),
            Def("Sensör", "Basınç transmiteri (4-20 mA)", "B", "Basınç transmiteri", [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Rect(-3, 3, 16, 9), Text(5, 8.8, "P/I", 2.6)], [Pin(0, 0, 0, -1, "+", Power), Pin(10, 0, 0, -1, "-", Signal)]),

            // ---- Otomasyon (5)
            Def(
                "Otomasyon",
                "Yumuşak yol verici (Soft starter)",
                "Q",
                "Yumuşak yol verici",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Line(20, 0, 20, 3), Rect(-3, 3, 26, 14), Text(10, 11, "SOFT", 3), Line(0, 17, 0, 20), Line(10, 17, 10, 20), Line(20, 17, 20, 20)],
                ThreePolePowerPins("L1", "U", "L2", "V", "L3", "W", height: 20)),
            Def(
                "Otomasyon",
                "HMI operatör paneli",
                "A",
                "HMI operatör paneli",
                [Line(0, 0, 0, 3), Line(10, 0, 10, 3), Line(20, 0, 20, 3), Rect(-3, 3, 26, 14), Rect(0, 5, 20, 9), Text(10, 10.5, "HMI", 2.8)],
                [Pin(0, 0, 0, -1, "24V", Power), Pin(10, 0, 0, -1, "0V", Power), Pin(20, 0, 0, -1, "ETH", new(PinKind.Communication, ConnectorType.Rj45))]),
            Def(
                "Otomasyon",
                "IO-Link master (4 port)",
                "A",
                "IO-Link master",
                [.. Enumerable.Range(0, 4).Select(i => Line(i * 5, 0, i * 5, 3)), Rect(-3, 3, 21, 9), Text(7.5, 9, "IO-L", 2.6), Line(0, 12, 0, 15), Line(10, 12, 10, 15)],
                [.. Enumerable.Range(1, 4).Select(i => Pin((i - 1) * 5, 0, 0, -1, $"X{i}", new(PinKind.Communication, ConnectorType.M12))), Pin(0, 15, 0, 1, "24V", Power), Pin(10, 15, 0, 1, "0V", Power)]),
            Def(
                "Otomasyon",
                "Artımlı enkoder",
                "B",
                "Enkoder",
                [Line(0, 0, 0, 3), Line(5, 0, 5, 3), Line(10, 0, 10, 3), Line(15, 0, 15, 3), Circle(7.5, 10, 7), Text(7.5, 11.2, "ENC", 2.4)],
                [Pin(0, 0, 0, -1, "24V", Power), Pin(5, 0, 0, -1, "0V", Power), Pin(10, 0, 0, -1, "A", Signal), Pin(15, 0, 0, -1, "B", Signal)]),
            Def(
                "Otomasyon",
                "Güvenlik ışık perdesi (OSSD)",
                "B",
                "Güvenlik ışık perdesi",
                [Line(0, 0, 0, 3), Line(5, 0, 5, 3), Line(10, 0, 10, 3), Line(15, 0, 15, 3), Rect(-3, 3, 21, 10), Text(7.5, 9.2, "OSSD", 2.4)],
                [Pin(0, 0, 0, -1, "24V", Power), Pin(5, 0, 0, -1, "0V", Power), Pin(10, 0, 0, -1, "OSSD1", Signal), Pin(15, 0, 0, -1, "OSSD2", Signal)]),
        ];
    }

    private static SymbolPin[] ThreePolePowerPins(
        string top1, string bottom1, string top2, string bottom2, string top3, string bottom3, double height = 15) =>
    [
        Pin(0, 0, 0, -1, top1, Power),
        Pin(0, height, 0, 1, bottom1, Power),
        Pin(10, 0, 0, -1, top2, Power),
        Pin(10, height, 0, 1, bottom2, Power),
        Pin(20, 0, 0, -1, top3, Power),
        Pin(20, height, 0, 1, bottom3, Power),
    ];

    private static SymbolShape[] MultiPoleContactShapes(int poles)
    {
        var shapes = new List<SymbolShape>();
        for (var i = 0; i < poles; i++)
        {
            var dx = i * 10.0;
            shapes.Add(Line(dx, 0, dx, 5));
            shapes.Add(Line(dx, 10, dx - 3, 4.5));
            shapes.Add(Line(dx, 10, dx, 15));
        }

        return [.. shapes];
    }

    private static SymbolShape[] ThreePoleBreakerShapes(bool isMotorBreaker)
    {
        var shapes = new List<SymbolShape>(MultiPoleContactShapes(3));
        shapes.Add(Line(-1.5, 7.5, 20, 7.5));
        for (var i = 0; i < 3; i++)
        {
            var dx = i * 10.0;
            shapes.Add(Line(dx - 1.5, 3, dx + 1.5, 6));
            shapes.Add(Line(dx - 1.5, 6, dx + 1.5, 3));
            if (isMotorBreaker)
            {
                shapes.Add(Rect(dx + 2.2, 6, 3.2, 3.2));
            }
        }

        return [.. shapes];
    }

    private static SymbolShape[] ThreePoleThermalShapes()
    {
        var shapes = new List<SymbolShape>();
        for (var i = 0; i < 3; i++)
        {
            var dx = i * 10.0;
            shapes.Add(Line(dx, 0, dx, 4.5));
            shapes.Add(Rect(dx - 3, 4.5, 6, 6));
            shapes.Add(Line(dx - 2, 7.5, dx, 5.5));
            shapes.Add(Line(dx, 5.5, dx + 2, 9.5));
            shapes.Add(Line(dx, 10.5, dx, 15));
        }

        return [.. shapes];
    }

    private static SymbolShape[] ThreePoleSwitchDisconnectorShapes()
    {
        var shapes = new List<SymbolShape>(MultiPoleContactShapes(3));
        shapes.Add(Line(-3, 7.5, 20, 7.5));
        for (var i = 0; i < 3; i++)
        {
            var dx = i * 10.0;
            shapes.Add(Line(dx - 1.8, 4.5, dx + 1.8, 4.5));
        }

        return [.. shapes];
    }
}
