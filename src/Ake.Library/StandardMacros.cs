using System.Security.Cryptography;
using System.Text;
using Ake.Core.Macros;
using Ake.Core.Model;

namespace Ake.Library;

/// <summary>
/// Editörle birlikte gelen hazır devre makroları ve seçilebilir değer kümeleri (ADR-0058).
/// Her makro değer kümesi seçildiğinde ürün sipariş kodu, akım veya özel özellikler cihazlara yazılır.
/// </summary>
public static class StandardMacros
{
    private static readonly Lazy<IReadOnlyList<Macro>> Catalog = new(Build);

    public static IReadOnlyList<Macro> All => Catalog.Value;

    private static IReadOnlyList<Macro> Build() =>
    [
        MotorStarterMacro(),
        Supply24VdcMacro(),
        EmergencyStopMacro(),
    ];

    private static Macro MotorStarterMacro()
    {
        var qOut = Pt("motor:q:2");
        var qMain = new Function(FnId("motor:q"), FunctionKind.Protection, [Pt("motor:q:1", "1"), Pt(qOut, "2")], passThrough: true, description: "Motor koruma şalteri", ratedCurrentA: 4m);
        var qDev = new Device(DevId("motor:q"), "-Q1", [qMain], description: "Motor koruma şalteri", part: new PartRef("Schneider", "GV2ME08"));

        var k1In = Pt("motor:k:l1");
        var k1Out = Pt("motor:k:t1");
        var kCoil = new Function(FnId("motor:k:coil"), FunctionKind.Coil, [Pt("motor:k:a1", "A1"), Pt("motor:k:a2", "A2")], description: "Kontaktör bobini");
        var kPower = new Function(FnId("motor:k:pwr"), FunctionKind.PowerContact, [Pt(k1In, "1/L1"), Pt(k1Out, "2/T1")], passThrough: true, description: "Ana kontak");
        var kDev = new Device(DevId("motor:k"), "-K1", [kCoil, kPower], description: "Motor kontaktörü", part: new PartRef("Schneider", "LC1D09M7"));

        var mIn = Pt("motor:m:u1");
        var mFn = new Function(FnId("motor:m:fn"), FunctionKind.Motor, [Pt(mIn, "U1"), Pt("motor:m:v1", "V1"), Pt("motor:m:w1", "W1")], description: "3~ Asenkron motor");
        var mDev = new Device(DevId("motor:m"), "-M1", [mFn], description: "1.5 kW motor");

        return new Macro(
            "Motor çıkışı",
            [qDev, kDev, mDev],
            [
                new Connection(ConnId("motor:c1"), qOut, k1In),
                new Connection(ConnId("motor:c2"), k1Out, mIn),
            ],
            [
                new MacroPlacement(qMain.Id, 0, 0),
                new MacroPlacement(kPower.Id, 0, 35),
                new MacroPlacement(kCoil.Id, 30, 35),
                new MacroPlacement(mFn.Id, 0, 70),
            ],
            [],
            "3~ motor doğrudan yol verme devresi (koruma + kontaktör + motor)",
            [
                new MacroValueSet("0.75 kW / 1.9 A (400 V)", new Dictionary<string, string>
                {
                    ["-Q1.ratedCurrentA"] = "2.5",
                    ["-Q1.orderCode"] = "GV2ME07",
                    ["-K1.orderCode"] = "LC1D09M7",
                    ["-M1.description"] = "0.75 kW motor",
                    ["-M1.prop.Güç"] = "0.75 kW",
                }),
                new MacroValueSet("1.5 kW / 3.5 A (400 V)", new Dictionary<string, string>
                {
                    ["-Q1.ratedCurrentA"] = "4",
                    ["-Q1.orderCode"] = "GV2ME08",
                    ["-K1.orderCode"] = "LC1D09M7",
                    ["-M1.description"] = "1.5 kW motor",
                    ["-M1.prop.Güç"] = "1.5 kW",
                }),
                new MacroValueSet("4.0 kW / 8.5 A (400 V)", new Dictionary<string, string>
                {
                    ["-Q1.ratedCurrentA"] = "10",
                    ["-Q1.orderCode"] = "GV2ME14",
                    ["-K1.orderCode"] = "LC1D18M7",
                    ["-M1.description"] = "4.0 kW motor",
                    ["-M1.prop.Güç"] = "4.0 kW",
                }),
            ]);
    }

    private static Macro Supply24VdcMacro()
    {
        var gOut = Pt("psu:g:24v");
        var gFn = new Function(FnId("psu:g:fn"), FunctionKind.PowerSupply, [Pt("psu:g:l", "L"), Pt("psu:g:n", "N"), Pt(gOut, "+24V"), Pt("psu:g:0v", "0V")], description: "24 VDC güç kaynağı");
        var gDev = new Device(DevId("psu:g"), "-G1", [gFn], description: "24VDC 5A güç kaynağı", part: new PartRef("Siemens", "6EP1333-2BA20"));

        var fIn = Pt("psu:f:1");
        var fFn = new Function(FnId("psu:f:fn"), FunctionKind.Protection, [Pt(fIn, "1"), Pt("psu:f:2", "2")], passThrough: true, description: "DC çıkış sigortası", ratedCurrentA: 5m);
        var fDev = new Device(DevId("psu:f"), "-F1", [fFn], description: "DC sigorta", part: new PartRef("Phoenix", "2907199"));

        return new Macro(
            "24VDC besleme",
            [gDev, fDev],
            [new Connection(ConnId("psu:c1"), gOut, fIn)],
            [
                new MacroPlacement(gFn.Id, 0, 0),
                new MacroPlacement(fFn.Id, 0, 40),
            ],
            [],
            "Anahtarlamalı 24 VDC güç kaynağı ve ikincil koruma sigortası",
            [
                new MacroValueSet("2.5 A (60 W)", new Dictionary<string, string>
                {
                    ["-G1.orderCode"] = "6EP1332-1SH43",
                    ["-G1.description"] = "24VDC 2.5A güç kaynağı",
                    ["-F1.ratedCurrentA"] = "2.5",
                }),
                new MacroValueSet("5 A (120 W)", new Dictionary<string, string>
                {
                    ["-G1.orderCode"] = "6EP1333-2BA20",
                    ["-G1.description"] = "24VDC 5A güç kaynağı",
                    ["-F1.ratedCurrentA"] = "5",
                }),
                new MacroValueSet("10 A (240 W)", new Dictionary<string, string>
                {
                    ["-G1.orderCode"] = "6EP1334-2BA20",
                    ["-G1.description"] = "24VDC 10A güç kaynağı",
                    ["-F1.ratedCurrentA"] = "10",
                }),
            ]);
    }

    private static Macro EmergencyStopMacro()
    {
        var sOut = Pt("estop:s:12");
        var sNc1 = new Function(FnId("estop:s:nc1"), FunctionKind.NcContact, [Pt("estop:s:11", "11"), Pt(sOut, "12")], description: "Acil stop NK 1");
        var sNc2 = new Function(FnId("estop:s:nc2"), FunctionKind.NcContact, [Pt("estop:s:21", "21"), Pt("estop:s:22", "22")], description: "Acil stop NK 2");
        var sDev = new Device(DevId("estop:s"), "-S1", [sNc1, sNc2], description: "Acil stop butonu", part: new PartRef("Siemens", "3SU1150-1HB20-1CG0"));

        var kA1 = Pt("estop:k:a1");
        var kFn = new Function(FnId("estop:k:fn"), FunctionKind.Coil, [Pt(kA1, "A1"), Pt("estop:k:a2", "A2")], description: "Emniyet rölesi");
        var kDev = new Device(DevId("estop:k"), "-K1", [kFn], description: "Emniyet rölesi", part: new PartRef("Pilz", "PNOZ-X2.8P-24VDC"));

        return new Macro(
            "Acil stop devresi",
            [sDev, kDev],
            [new Connection(ConnId("estop:c1"), sOut, kA1)],
            [
                new MacroPlacement(sNc1.Id, 0, 0),
                new MacroPlacement(sNc2.Id, 20, 0),
                new MacroPlacement(kFn.Id, 0, 35),
            ],
            [],
            "Çift kanallı acil stop butonu ve emniyet rölesi",
            [
                new MacroValueSet("24 VDC emniyet", new Dictionary<string, string>
                {
                    ["-K1.orderCode"] = "PNOZ-X2.8P-24VDC",
                    ["-K1.prop.Besleme"] = "24 VDC",
                }),
                new MacroValueSet("230 VAC emniyet", new Dictionary<string, string>
                {
                    ["-K1.orderCode"] = "PNOZ-X3-230VAC",
                    ["-K1.prop.Besleme"] = "230 VAC",
                }),
            ]);
    }

    private static ConnectionPoint Pt(ConnectionPointId id, string name) => new(id, name);
    private static ConnectionPoint Pt(string key, string name) => new(Pt(key), name);
    private static ConnectionPointId Pt(string key) => new(StableGuid("pt:" + key));
    private static FunctionId FnId(string key) => new(StableGuid("fn:" + key));
    private static DeviceId DevId(string key) => new(StableGuid("dev:" + key));
    private static ConnectionId ConnId(string key) => new(StableGuid("conn:" + key));

    private static Guid StableGuid(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ake-std-macro:" + key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
