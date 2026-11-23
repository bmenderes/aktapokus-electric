using Ake.Core.Catalog;
using Ake.Core.Model;

namespace Ake.Library;

/// <summary>
/// ETIM sınıflandırma kodlarını (ör. EC000066 kontaktör, EC000042 devre kesici) editörün
/// <see cref="FunctionTemplate"/> listesine ve kütüphane kategorisine eşler (ADR-0059).
/// </summary>
public static class EtimTemplateMapper
{
    public static IReadOnlyList<FunctionTemplate> TemplatesForClass(
        string? etimClass,
        string? className = null,
        decimal? ratedCurrentA = null)
    {
        var code = etimClass?.Trim().ToUpperInvariant() ?? string.Empty;
        switch (code)
        {
            case "EC000066": // Kontaktör / yardımcı röle
            case "EC001437": // Modüler kontaktör
                return
                [
                    new(FunctionKind.Coil, ["A1", "A2"], Description: "Bobin"),
                    new(FunctionKind.PowerContact, ["1/L1", "2/T1"], PassThrough: true, RatedCurrentA: ratedCurrentA),
                    new(FunctionKind.PowerContact, ["3/L2", "4/T2"], PassThrough: true, RatedCurrentA: ratedCurrentA),
                    new(FunctionKind.PowerContact, ["5/L3", "6/T3"], PassThrough: true, RatedCurrentA: ratedCurrentA),
                    new(FunctionKind.NoContact, ["13", "14"], Description: "Yardımcı NA"),
                ];

            case "EC000042": // Minyatür devre kesici (MCB)
            case "EC000228": // Kompakt devre kesici (MCCB)
                return
                [
                    new(FunctionKind.Protection, ["1", "2"], PassThrough: true, RatedCurrentA: ratedCurrentA, Description: "Faz 1"),
                    new(FunctionKind.Protection, ["3", "4"], PassThrough: true, RatedCurrentA: ratedCurrentA, Description: "Faz 2"),
                    new(FunctionKind.Protection, ["5", "6"], PassThrough: true, RatedCurrentA: ratedCurrentA, Description: "Faz 3"),
                ];

            case "EC000074": // Motor koruma şalteri
            case "EC002540": // Termik aşırı yük rölesi
                return
                [
                    new(FunctionKind.Protection, ["1/L1", "2/T1"], PassThrough: true, RatedCurrentA: ratedCurrentA),
                    new(FunctionKind.Protection, ["3/L2", "4/T2"], PassThrough: true, RatedCurrentA: ratedCurrentA),
                    new(FunctionKind.Protection, ["5/L3", "6/T3"], PassThrough: true, RatedCurrentA: ratedCurrentA),
                    new(FunctionKind.NcContact, ["95", "96"], Description: "Hata kontağı"),
                ];

            case "EC001032": // Kumanda rölesi / zaman rölesi
            case "EC001439":
                return
                [
                    new(FunctionKind.Coil, ["A1", "A2"], Description: "Bobin"),
                    new(FunctionKind.NoContact, ["11", "14"], Description: "NA kontak"),
                    new(FunctionKind.NcContact, ["11", "12"], Description: "NK kontak"),
                ];

            case "EC000897": // Geçiş klemensi
            case "EC000902": // Toprak klemensi
                return
                [
                    new(FunctionKind.Terminal, ["1", "2"], PassThrough: true, RatedCurrentA: ratedCurrentA, Description: "Klemens"),
                ];

            case "EC001419": // Güç kaynağı
            case "EC002541":
                return
                [
                    new(FunctionKind.PowerSupply, ["L", "N", "PE", "+24V", "0V"], RatedCurrentA: ratedCurrentA, Description: "Güç kaynağı"),
                ];

            case "EC001099": // Endüktif / kapasitif / optik sensör
            case "EC002714":
            case "EC001849":
                return
                [
                    new(FunctionKind.Generic, ["+", "-", "Q"], RatedCurrentA: ratedCurrentA, Description: "Sensör"),
                ];

            case "EC001418": // PLC I/O modülü
            case "EC000236": // PLC CPU
            case "EC001604": // Fieldbus I/O
                return
                [
                    new(FunctionKind.PlcChannel, ["X1", "X2", "24V", "0V"], RatedCurrentA: ratedCurrentA, Description: "PLC / Fieldbus"),
                ];

            case "EC001857": // Frekans konvertörü / servo sürücü
            case "EC002546":
                return
                [
                    new(FunctionKind.Generic, ["L1", "L2", "L3", "U", "V", "W", "PE"], RatedCurrentA: ratedCurrentA, Description: "Sürücü"),
                ];

            case "EC001449": // Elektrik motoru
            case "EC001851":
                return
                [
                    new(FunctionKind.Motor, ["U1", "V1", "W1", "PE"], RatedCurrentA: ratedCurrentA, Description: "Motor"),
                ];
        }

        // ETIM kodu tanınmıyorsa sınıf adı üzerinden kestirim yap.
        var hint = className?.Trim().ToLowerInvariant() ?? string.Empty;
        if (hint.Contains("contactor") || hint.Contains("kontaktör") || hint.Contains("schütz"))
        {
            return TemplatesForClass("EC000066", null, ratedCurrentA);
        }

        if (hint.Contains("breaker") || hint.Contains("kesici") || hint.Contains("schalter"))
        {
            return TemplatesForClass("EC000042", null, ratedCurrentA);
        }

        if (hint.Contains("terminal") || hint.Contains("klemens") || hint.Contains("klemme"))
        {
            return TemplatesForClass("EC000897", null, ratedCurrentA);
        }

        if (hint.Contains("power supply") || hint.Contains("güç kaynağ") || hint.Contains("netzteil"))
        {
            return TemplatesForClass("EC001419", null, ratedCurrentA);
        }

        if (hint.Contains("plc") || hint.Contains("fieldbus") || hint.Contains("profinet") || hint.Contains("i/o"))
        {
            return TemplatesForClass("EC001418", null, ratedCurrentA);
        }

        if (hint.Contains("motor"))
        {
            return TemplatesForClass("EC001449", null, ratedCurrentA);
        }

        if (hint.Contains("sensor") || hint.Contains("sensör"))
        {
            return TemplatesForClass("EC001099", null, ratedCurrentA);
        }

        if (hint.Contains("drive") || hint.Contains("inverter") || hint.Contains("sürücü"))
        {
            return TemplatesForClass("EC001857", null, ratedCurrentA);
        }

        return [new(FunctionKind.Generic, ["1", "2"], RatedCurrentA: ratedCurrentA, Description: className)];
    }

    public static string CategoryForClass(string? etimClass, string? fallback = null) =>
        (etimClass?.Trim().ToUpperInvariant() ?? string.Empty) switch
        {
            "EC000066" or "EC001437" or "EC001032" or "EC001439" => "Kumanda / Röle",
            "EC000042" or "EC000228" or "EC000074" or "EC002540" => "Koruma",
            "EC000897" or "EC000902" => "Klemens",
            "EC001419" or "EC002541" => "Güç",
            "EC001099" or "EC002714" or "EC001849" => "Sensör",
            "EC001418" or "EC000236" or "EC001604" => "Otomasyon",
            "EC001857" or "EC002546" or "EC001449" or "EC001851" => "Motor / Sürücü",
            _ => string.IsNullOrWhiteSpace(fallback) ? "Üretici kataloğu" : fallback.Trim(),
        };
}
