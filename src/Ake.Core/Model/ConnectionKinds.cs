using System.Text.RegularExpressions;

namespace Ake.Core.Model;

public readonly record struct ConnectionKindId(Guid Value)
{
    public static ConnectionKindId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Bağlantının çizgi biçimi (şemada ve haberleşme şemasında).</summary>
public enum LineStyle
{
    Solid,
    Dashed,
    DashDot,
    Dotted,
}

/// <summary>
/// Bağlantı (kablo) türü (ADR-0036): ad, renk ve çizgi biçimi. Bir türe atanan bağlantılar şemada türün rengi ve çizgi biçimiyle çizilir
/// (Profinet düz yeşil, DriveCLiQ kesik gri gibi); türler sayfada otomatik gösterge olarak listelenir. Renk bilgisi okumada ve
/// haberleşme/tek hat şemalarının üretiminde de kullanılır.
/// </summary>
public sealed record ConnectionKind(
    ConnectionKindId Id,
    string Name,
    string Color,
    LineStyle Style = LineStyle.Solid,
    PinKind Category = PinKind.Unspecified)
{
    public const int MaxNameLength = 60;

    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>Geçerliyse <c>null</c>.</summary>
    public Error? Check() =>
        string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength
            ? new Error("kind.ad_gecersiz", $"Bağlantı türünün adı boş olamaz ve en çok {MaxNameLength} karakter olabilir.")
        : !HexColor.IsMatch(Color ?? string.Empty)
            ? new Error("kind.renk_gecersiz", $"'{Name}' türünün rengi #RRGGBB biçiminde olmalı: '{Color}'")
        : !Enum.IsDefined(Style) || !Enum.IsDefined(Category)
            ? new Error("kind.deger_gecersiz", $"'{Name}' türünün çizgi biçimi ya da kategorisi bilinmiyor.")
            : null;

    /// <summary>Hazır bağlantı türleri (otomasyon ve pano): kullanıcı bunları tek komutla projeye ekleyebilir.</summary>
    public static IReadOnlyList<ConnectionKind> Standard { get; } =
    [
        Make("Profinet", "#2E9E2E", LineStyle.Solid, PinKind.Communication),
        Make("Ethernet", "#1B5E20", LineStyle.DashDot, PinKind.Communication),
        Make("EtherCAT", "#E8780C", LineStyle.Solid, PinKind.Communication),
        Make("CANopen", "#8E24AA", LineStyle.Solid, PinKind.Communication),
        Make("RS-485 / Modbus", "#6D4C41", LineStyle.Solid, PinKind.Communication),
        Make("DriveCLiQ", "#808080", LineStyle.Dashed, PinKind.Communication),
        Make("Güvenlik", "#F9A825", LineStyle.Solid, PinKind.Signal),
        Make("24 VDC", "#D62828", LineStyle.Solid, PinKind.Power),
        Make("0 V", "#1F4FD8", LineStyle.Solid, PinKind.Power),

        // Pnömatik hatlar (ISO 1219-1): besleme/çalışma hattı düz, pilot (kumanda) hattı kesik, egzoz hattı noktalı (ADR-0052).
        Make("Hava hattı", "#0B6FB8", LineStyle.Solid, PinKind.Pneumatic),
        Make("Pilot hattı", "#0B6FB8", LineStyle.Dashed, PinKind.Pneumatic),
        Make("Egzoz hattı", "#6E8EA6", LineStyle.Dotted, PinKind.Pneumatic),
    ];

    // Kimlik addan türetilir: standart tür her projeye aynı kimlikle girer; tekrar eklemek çoğaltmaz.
    private static ConnectionKind Make(string name, string color, LineStyle style, PinKind category) =>
        new(
            new ConnectionKindId(new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("aktapokus.kablo-turu/" + name)))),
            name,
            color,
            style,
            category);
}
