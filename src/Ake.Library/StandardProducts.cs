using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Model;

namespace Ake.Library;

/// <summary>
/// Editörle birlikte gelen standart endüstriyel ürün kataloğu (Faz 6b-4, ADR-0056):
/// kamuya açık üretici katalog özetleriyle kontaktör, röle, koruma, güç kaynağı, sensör (M8/M12),
/// kumanda, motor/sürücü, Ethernet anahtarı, PLC/GÇ, klemens ve pnömatik ürünleri içerir.
/// </summary>
public static class StandardProducts
{
    public const string Source = "aktapokus-electric standart ürün kataloğu (kamuya açık teknik katalog verisi)";
    public const string License = "Apache-2.0 (kamuya açık teknik veri özeti)";

    private static readonly Lazy<IReadOnlyList<LibraryProduct>> Catalog = new(Build);

    /// <summary>Tüm standart ürün kayıtları.</summary>
    public static IReadOnlyList<LibraryProduct> All => Catalog.Value;

    /// <summary>Standart ürünleri kütüphane dosyasına ekler (var olan üretici+sipariş kodu kayıtlarını günceller).</summary>
    public static Result EnsureInstalled(string libraryPath)
    {
        ArgumentNullException.ThrowIfNull(libraryPath);

        var opened = ComponentLibrary.Open(libraryPath);
        if (opened.IsFailure)
        {
            return Result.Failure(opened.Error!);
        }

        using var library = opened.Value;
        var imported = library.Import(All);
        return imported.IsFailure ? Result.Failure(imported.Error!) : Result.Success();
    }

    private static FunctionTemplate Fn(
        FunctionKind kind, string description, params string[] pins) =>
        new(kind, pins, Description: description);

    private static FunctionTemplate Pass(
        FunctionKind kind, string description, params string[] pins) =>
        new(kind, pins, PassThrough: true, Description: description);

    private static ProductProperty Prop(string key, string value, string? unit = null) =>
        new(key, value, unit);

    private static LibraryProduct Item(
        string manufacturer,
        string orderCode,
        string? typeDesignation,
        string description,
        string category,
        FunctionTemplate[] functions,
        Mounting? mounting = null,
        params ProductProperty[] properties) =>
        new(
            new ProductDefinition(manufacturer, orderCode, functions, typeDesignation, description, mounting),
            Source,
            License,
            category,
            properties);

    private static FunctionTemplate[] ContactorFunctions(string noDesc = "Yardımcı NO", string ncDesc = "Yardımcı NC") =>
    [
        Fn(FunctionKind.Coil, "Bobin", "A1", "A2"),
        Fn(FunctionKind.PowerContact, "Kutup 1", "1", "2"),
        Fn(FunctionKind.PowerContact, "Kutup 2", "3", "4"),
        Fn(FunctionKind.PowerContact, "Kutup 3", "5", "6"),
        Fn(FunctionKind.NoContact, noDesc, "13", "14"),
        Fn(FunctionKind.NcContact, ncDesc, "21", "22"),
    ];

    private static FunctionTemplate[] SensorFunctions(string description) =>
    [
        Fn(FunctionKind.Generic, description, "1", "3", "4"),
    ];

    private static FunctionTemplate[] PlcModuleFunctions(string prefix, int channels) =>
    [
        .. Enumerable.Range(1, channels).Select(i => Fn(FunctionKind.PlcChannel, $"{prefix} {i}", i.ToString(System.Globalization.CultureInfo.InvariantCulture))),
    ];

    private static IReadOnlyList<LibraryProduct> Build() =>
    [
        // ---- Güç kaynağı
        Item(
            "Siemens", "6EP1333-2BA20", "SITOP PSU100S 24V/5A",
            "Güç kaynağı, 120/230 VAC giriş → 24 VDC 5 A çıkış",
            "Güç kaynağı",
            [Fn(FunctionKind.PowerSupply, "1 AC / 24 VDC 5 A", "L1", "N", "PE", "+", "-")],
            new Mounting(MountingType.DinRail35, 50, 125, 120, ClearanceMm: 20, HeatLossW: 12),
            Prop("inputVoltage", "120/230", "VAC"), Prop("outputVoltage", "24", "VDC"), Prop("outputCurrent", "5", "A")),
        Item(
            "Phoenix Contact", "2904601", "QUINT4-PS/1AC/24DC/10",
            "Güç kaynağı, 100-240 VAC → 24 VDC 10 A",
            "Güç kaynağı",
            [Fn(FunctionKind.PowerSupply, "1 AC / 24 VDC 10 A", "L", "N", "PE", "+", "-")],
            new Mounting(MountingType.DinRail35, 50, 130, 125, ClearanceMm: 20, HeatLossW: 18),
            Prop("outputVoltage", "24", "VDC"), Prop("outputCurrent", "10", "A")),

        // ---- Kontaktör ve röle
        Item(
            "Schneider Electric", "LC1D09BD", "TeSys D 9 A, 24 VDC bobin",
            "Kontaktör, 3 kutup, 1 NO + 1 NC yardımcı kontak",
            "Kontaktör ve röle",
            ContactorFunctions(),
            new Mounting(MountingType.DinRail35, 45, 77, 95, HeatLossW: 2.5),
            Prop("ratedCurrentAc3", "9", "A"), Prop("coilVoltage", "24", "VDC")),
        Item(
            "Schneider Electric", "LC1D18BD", "TeSys D 18 A, 24 VDC bobin",
            "Kontaktör, 3 kutup, 1 NO + 1 NC yardımcı kontak",
            "Kontaktör ve röle",
            ContactorFunctions(),
            new Mounting(MountingType.DinRail35, 45, 77, 95, HeatLossW: 3.5),
            Prop("ratedCurrentAc3", "18", "A"), Prop("coilVoltage", "24", "VDC")),
        Item(
            "Siemens", "3RT2016-1BB41", "SIRIUS 3RT2 9 A, 24 VDC",
            "Güç kontaktörü, AC-3 9 A / 4 kW, 1 NO yardımcı kontak",
            "Kontaktör ve röle",
            [
                Fn(FunctionKind.Coil, "Bobin", "A1", "A2"),
                Fn(FunctionKind.PowerContact, "Kutup 1", "1", "2"),
                Fn(FunctionKind.PowerContact, "Kutup 2", "3", "4"),
                Fn(FunctionKind.PowerContact, "Kutup 3", "5", "6"),
                Fn(FunctionKind.NoContact, "Yardımcı NO", "13", "14"),
            ],
            new Mounting(MountingType.DinRail35, 45, 58, 73, HeatLossW: 2.4),
            Prop("ratedCurrentAc3", "9", "A"), Prop("coilVoltage", "24", "VDC")),
        Item(
            "Finder", "40.52.9.024.0000", "40.52 Mini röle 2 CO 8 A",
            "Endüstriyel ara röle, 24 VDC bobin, 2 değiştirmeli kontak",
            "Kontaktör ve röle",
            [
                Fn(FunctionKind.Coil, "Bobin", "A1", "A2"),
                Fn(FunctionKind.Generic, "Kontak 1 (CO)", "11", "12", "14"),
                Fn(FunctionKind.Generic, "Kontak 2 (CO)", "21", "22", "24"),
            ],
            new Mounting(MountingType.DinRail35, 15.5, 80, 62, HeatLossW: 0.7),
            Prop("coilVoltage", "24", "VDC"), Prop("ratedCurrent", "8", "A")),
        Item(
            "Omron", "G2R-2-SN-24DC", "G2R-2-SN 24 VDC",
            "Güç rölesi, 24 VDC bobin, 2 CO 5 A, soketli",
            "Kontaktör ve röle",
            [
                Fn(FunctionKind.Coil, "Bobin", "A1", "A2"),
                Fn(FunctionKind.Generic, "Kontak 1 (CO)", "11", "12", "14"),
                Fn(FunctionKind.Generic, "Kontak 2 (CO)", "21", "22", "24"),
            ],
            new Mounting(MountingType.DinRail35, 16, 78, 65, HeatLossW: 0.6),
            Prop("coilVoltage", "24", "VDC"), Prop("ratedCurrent", "5", "A")),
        Item(
            "Pilz", "774306", "PNOZ X2.8P 24VACDC",
            "Emniyet rölesi, çift kanallı acil stop izleme, 3 NO emniyet çıkışı",
            "Kontaktör ve röle",
            [
                Fn(FunctionKind.PowerSupply, "Besleme", "A1", "A2"),
                Fn(FunctionKind.Generic, "Giriş kanalları", "S11", "S12", "S21", "S22"),
                Fn(FunctionKind.Generic, "Reset / Geri besleme", "Y1", "Y2", "S34"),
                Fn(FunctionKind.NoContact, "Emniyet NO 1", "13", "14"),
                Fn(FunctionKind.NoContact, "Emniyet NO 2", "23", "24"),
                Fn(FunctionKind.NoContact, "Emniyet NO 3", "33", "34"),
            ],
            new Mounting(MountingType.DinRail35, 22.5, 94, 121, HeatLossW: 2.5),
            Prop("safetyCategory", "PL e / SIL 3"), Prop("supplyVoltage", "24", "VAC/DC")),

        // ---- Koruma
        Item(
            "Schneider Electric", "GV2ME10", "TeSys GV2ME 4-6.3 A",
            "Motor koruma şalteri, 3 kutup, termik manyetik",
            "Koruma",
            [
                new FunctionTemplate(FunctionKind.Protection, ["1", "2"], Description: "Kutup 1", RatedCurrentA: 6.3m),
                new FunctionTemplate(FunctionKind.Protection, ["3", "4"], Description: "Kutup 2", RatedCurrentA: 6.3m),
                new FunctionTemplate(FunctionKind.Protection, ["5", "6"], Description: "Kutup 3", RatedCurrentA: 6.3m),
            ],
            new Mounting(MountingType.DinRail35, 45, 89, 78.5, HeatLossW: 2.4),
            Prop("settingRangeMin", "4", "A"), Prop("settingRangeMax", "6.3", "A")),
        Item(
            "Schneider Electric", "GV2ME14", "TeSys GV2ME 6-10 A",
            "Motor koruma şalteri, 3 kutup, termik manyetik",
            "Koruma",
            [
                new FunctionTemplate(FunctionKind.Protection, ["1", "2"], Description: "Kutup 1", RatedCurrentA: 10m),
                new FunctionTemplate(FunctionKind.Protection, ["3", "4"], Description: "Kutup 2", RatedCurrentA: 10m),
                new FunctionTemplate(FunctionKind.Protection, ["5", "6"], Description: "Kutup 3", RatedCurrentA: 10m),
            ],
            new Mounting(MountingType.DinRail35, 45, 89, 78.5, HeatLossW: 3.1),
            Prop("settingRangeMin", "6", "A"), Prop("settingRangeMax", "10", "A")),
        Item(
            "Siemens", "5SY4106-7", "5SY4 1P C6",
            "Otomatik sigorta, 1 kutup, C karakteristiği, 6 A, 10 kA",
            "Koruma",
            [new FunctionTemplate(FunctionKind.Protection, ["1", "2"], Description: "1P C6A", RatedCurrentA: 6m)],
            new Mounting(MountingType.DinRail35, 18, 90, 76, HeatLossW: 1.4),
            Prop("curve", "C"), Prop("ratedCurrent", "6", "A")),
        Item(
            "Siemens", "5SY4316-7", "5SY4 3P C16",
            "Otomatik sigorta, 3 kutup, C karakteristiği, 16 A, 10 kA",
            "Koruma",
            [
                new FunctionTemplate(FunctionKind.Protection, ["1", "2"], Description: "L1", RatedCurrentA: 16m),
                new FunctionTemplate(FunctionKind.Protection, ["3", "4"], Description: "L2", RatedCurrentA: 16m),
                new FunctionTemplate(FunctionKind.Protection, ["5", "6"], Description: "L3", RatedCurrentA: 16m),
            ],
            new Mounting(MountingType.DinRail35, 54, 90, 76, HeatLossW: 3.9),
            Prop("curve", "C"), Prop("ratedCurrent", "16", "A")),
        Item(
            "Schneider Electric", "LRD10", "TeSys LRD 4-6 A",
            "Termik aşırı akım rölesi, 3 kutup + 1 NO / 1 NC",
            "Koruma",
            [
                Fn(FunctionKind.Protection, "Güç devresi 1", "1", "2"),
                Fn(FunctionKind.Protection, "Güç devresi 2", "3", "4"),
                Fn(FunctionKind.Protection, "Güç devresi 3", "5", "6"),
                Fn(FunctionKind.NoContact, "Hata sinyali NO", "97", "98"),
                Fn(FunctionKind.NcContact, "Açtırma kontağı NC", "95", "96"),
            ],
            new Mounting(MountingType.DinRail35, 45, 66, 70, HeatLossW: 2.0),
            Prop("settingRange", "4-6", "A")),

        // ---- Sensör (M8 / M12)
        Item(
            "Sick", "IME12-04BPSZC0S", "IME12 M12 PNP NO",
            "Endüktif yaklaşım sensörü, M12 4 pin konnektörlü, 4 mm, PNP NO",
            "Sensör",
            SensorFunctions("M12 Endüktif sensör (1:+24V, 3:0V, 4:PNP)"),
            null,
            Prop("connector", "M12 4-pin"), Prop("output", "PNP NO"), Prop("supplyVoltage", "10-30", "VDC")),
        Item(
            "ifm electronic", "IGT200", "IGKC005BASKG/US-104",
            "Endüktif sensör, M12 konnektörlü, 5 mm, PNP NO",
            "Sensör",
            SensorFunctions("M12 Endüktif sensör (1:+24V, 3:0V, 4:PNP)"),
            null,
            Prop("connector", "M12 4-pin"), Prop("output", "PNP NO")),
        Item(
            "Sick", "WL12-3P2431", "WL12-3 M12 Reflektörlü fotosel",
            "Reflektörlü optik fotosel, M12 4 pin konnektörlü, PNP",
            "Sensör",
            SensorFunctions("M12 Optik fotosel (1:+24V, 3:0V, 4:Q)"),
            null,
            Prop("connector", "M12 4-pin"), Prop("output", "PNP")),
        Item(
            "Balluff", "BES01C8", "BES M08MI-PSC20B-S49G",
            "M8 3 pin konnektörlü endüktif sensör, 2 mm, PNP NO",
            "Sensör",
            SensorFunctions("M8 Endüktif sensör (1:+24V, 3:0V, 4:Q)"),
            null,
            Prop("connector", "M8 3-pin"), Prop("output", "PNP NO")),
        Item(
            "Festo", "543862", "SME-8M-DS-24V-K-0,3-M8D",
            "Manyetik reed silindir sensörü, T-kanallı, M8 3 pin konnektörlü",
            "Sensör",
            SensorFunctions("M8 Silindir sensörü (1:+24V, 3:0V, 4:Çıkış)"),
            null,
            Prop("connector", "M8 3-pin"), Prop("supplyVoltage", "24", "VDC")),

        // ---- Kumanda ve sinyal
        Item(
            "Schneider Electric", "XB4BA31", "Harmony XB4 Yeşil Start",
            "Metal başlıklı basma düğmesi, yeşil, 1 NO kontak",
            "Kumanda ve sinyal",
            [Fn(FunctionKind.NoContact, "Start NO", "3", "4")],
            new Mounting(MountingType.Door, 30, 47, 52),
            Prop("diameter", "22", "mm")),
        Item(
            "Schneider Electric", "XB4BS8442", "Harmony XB4 Acil Stop",
            "Acil stop mantar butonu, çevirmeli, 1 NC zorlamalı açma",
            "Kumanda ve sinyal",
            [Fn(FunctionKind.NcContact, "Acil Stop NC", "1", "2")],
            new Mounting(MountingType.Door, 40, 47, 82),
            Prop("diameter", "22", "mm")),
        Item(
            "Schneider Electric", "XB4BVB4", "Harmony XB4 Kırmızı LED 24V",
            "Kırmızı LED sinyal lambası, 24 VAC/DC",
            "Kumanda ve sinyal",
            [Fn(FunctionKind.Generic, "LED Lamba", "X1", "X2")],
            new Mounting(MountingType.Door, 30, 47, 54),
            Prop("supplyVoltage", "24", "VAC/DC")),

        // ---- Motor ve sürücü
        Item(
            "Schneider Electric", "ATV320U07N4B", "Altivar 320 0.75 kW",
            "Frekans invertörü (VFD), 3 faz 380-500 V, 0.75 kW",
            "Motor ve sürücü",
            [
                Fn(FunctionKind.PowerSupply, "Şebeke girişi", "R/L1", "S/L2", "T/L3", "PE"),
                Fn(FunctionKind.Motor, "Motor çıkışı", "U/T1", "V/T2", "W/T3"),
                Fn(FunctionKind.PlcChannel, "Dijital / analog girişler", "DI1", "DI2", "+24V", "COM"),
            ],
            new Mounting(MountingType.Plate, 45, 325, 245, ClearanceMm: 50, HeatLossW: 35),
            Prop("ratedPower", "0.75", "kW"), Prop("inputVoltage", "400", "VAC")),
        Item(
            "Siemens", "1LE1001-0DA22-2AA4", "SIMOTICS GP 0.75 kW 400V",
            "Üç fazlı asenkron motor, 0.75 kW, 1500 dev/dak, IE3",
            "Motor ve sürücü",
            [Fn(FunctionKind.Motor, "Stator 3~", "U1", "V1", "W1", "PE")],
            null,
            Prop("ratedPower", "0.75", "kW"), Prop("ratedCurrent", "1.75", "A"), Prop("ratedVoltage", "400", "VAC")),

        // ---- Haberleşme ve Ethernet
        Item(
            "Siemens", "6GK5005-0BA00-1AB2", "SCALANCE XB005",
            "Endüstriyel yönetilemez Ethernet anahtarı, 5x RJ45 10/100 Mbps, 24 VDC",
            "Haberleşme",
            [
                Fn(FunctionKind.Generic, "Besleme", "L+", "M", "FE"),
                Fn(FunctionKind.Generic, "Ethernet portları", "P1", "P2", "P3", "P4", "P5"),
            ],
            new Mounting(MountingType.DinRail35, 45, 100, 87, HeatLossW: 2.0),
            Prop("ports", "5x RJ45"), Prop("supplyVoltage", "24", "VDC")),
        Item(
            "Phoenix Contact", "2891001", "FL SWITCH 1005N",
            "Endüstriyel Ethernet anahtarı, 5x RJ45 10/100 Mbps, dar tasarım",
            "Haberleşme",
            [
                Fn(FunctionKind.Generic, "Besleme", "24V", "0V"),
                Fn(FunctionKind.Generic, "Ethernet portları", "P1", "P2", "P3", "P4", "P5"),
            ],
            new Mounting(MountingType.DinRail35, 22.5, 117, 84, HeatLossW: 1.8),
            Prop("ports", "5x RJ45"), Prop("supplyVoltage", "24", "VDC")),

        // ---- PLC ve G/Ç
        Item(
            "Beckhoff", "CX5130", "CX5130 Embedded PC",
            "Intel Atom gömülü PLC kontrolör, 2x Ethernet, E-bus",
            "PLC ve G/Ç",
            [Fn(FunctionKind.Generic, "Sistem beslemesi", "24V", "0V")],
            new Mounting(MountingType.DinRail35, 142, 100, 92, HeatLossW: 14),
            Prop("bus", "EtherCAT")),
        Item(
            "Beckhoff", "EK1100", "EK1100 EtherCAT Coupler",
            "EtherCAT kuplör modülü, RJ45 portlu",
            "PLC ve G/Ç",
            [Fn(FunctionKind.Generic, "Besleme", "24V", "0V")],
            new Mounting(MountingType.DinRail35, 44, 100, 68, HeatLossW: 2),
            Prop("bus", "EtherCAT")),
        Item(
            "Beckhoff", "EL1008", "EL1008 8x DI",
            "EtherCAT 8 kanal dijital giriş terminali, 24 VDC",
            "PLC ve G/Ç",
            PlcModuleFunctions("DI", 8),
            new Mounting(MountingType.DinRail35, 12, 100, 68, HeatLossW: 1),
            Prop("channels", "8"), Prop("signalType", "DI")),
        Item(
            "Beckhoff", "EL2008", "EL2008 8x DO",
            "EtherCAT 8 kanal dijital çıkış terminali, 24 VDC, 0.5 A",
            "PLC ve G/Ç",
            PlcModuleFunctions("DO", 8),
            new Mounting(MountingType.DinRail35, 12, 100, 68, HeatLossW: 1.5),
            Prop("channels", "8"), Prop("signalType", "DO")),

        // ---- Klemens ve pnömatik
        Item(
            "Phoenix Contact", "3044076", "UT 2,5",
            "Vidalı geçiş klemensi, 2.5 mm², gri",
            "Klemens ve pnömatik",
            [Pass(FunctionKind.Terminal, "Klemens", "a", "b")],
            new Mounting(MountingType.DinRail35, 5.2, 47.7, 47.5),
            Prop("crossSection", "2.5", "mm²")),
        Item(
            "Phoenix Contact", "3044092", "UT 2,5-PE",
            "Koruma topraklaması klemensi (PE), 2.5 mm², yeşil-sarı",
            "Klemens ve pnömatik",
            [Pass(FunctionKind.Terminal, "PE Klemens", "a", "b")],
            new Mounting(MountingType.DinRail35, 5.2, 47.7, 47.5),
            Prop("crossSection", "2.5", "mm²"), Prop("role", "PE")),
        Item(
            "Festo", "574354", "VUVG-L14-M52-AT-G18-1H3L-S",
            "5/2 tek selenoid pnömatik valf, 24 VDC, M8 konnektörlü",
            "Klemens ve pnömatik",
            [
                Fn(FunctionKind.Coil, "Selenoid bobin 24 VDC", "+", "-"),
                Fn(FunctionKind.Generic, "Pnömatik portlar", "4", "2", "5", "1", "3"),
            ],
            null,
            Prop("coilVoltage", "24", "VDC"), Prop("valveType", "5/2 monostabil")),
    ];
}
