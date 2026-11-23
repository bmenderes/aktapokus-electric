using System.Globalization;
using Ake.Core.Numbering;

namespace Ake.Core.Model;

/// <summary>
/// Tel numaralandırma ayarları (ADR-0041): kod (önek), başlangıç, adım ve basamak sayısı. "W", başlangıç 100, 3 hane → W100, W101…
/// Proje ayarıdır; yeni teller numaralanırken ve baştan numaralanırken kullanılır.
/// </summary>
/// <param name="Prefix">Numaranın önündeki kod (boşluksuz, en çok 12 karakter); boş olabilir.</param>
/// <param name="Start">İlk numara.</param>
/// <param name="Step">Numaralar arası artış.</param>
/// <param name="Width">Sayının en az basamak sayısı (sıfırla doldurulur); 0: doldurma yok.</param>
/// <param name="Scheme">Potansiyel bazlı ya da sıralı.</param>
public sealed record WireNumberSettings(
    string Prefix = "",
    int Start = 1,
    int Step = 1,
    int Width = 0,
    WireNumberingScheme Scheme = WireNumberingScheme.PerPotential)
{
    public const int MaxPrefixLength = 12;

    public const int MaxWidth = 8;

    /// <summary>Geçerliyse <c>null</c>.</summary>
    public Error? Check() =>
        Prefix is null || Prefix.Length > MaxPrefixLength || Prefix.Any(char.IsWhiteSpace)
            ? new Error("numara.kod_gecersiz", $"Tel numarası kodu en çok {MaxPrefixLength} karakter olmalı ve boşluk içermemeli: '{Prefix}'")
        : Start < 0 ? new Error("numara.baslangic_gecersiz", $"Başlangıç numarası negatif olamaz: {Start}")
        : Step < 1 ? new Error("numara.adim_gecersiz", $"Adım 1 ya da daha büyük olmalı: {Step}")
        : Width is < 0 or > MaxWidth ? new Error("numara.basamak_gecersiz", $"Basamak sayısı 0 ile {MaxWidth} arasında olmalı: {Width}")
        : !Enum.IsDefined(Scheme) ? new Error("numara.sema_gecersiz", $"Bilinmeyen numaralandırma şeması: {Scheme}")
        : null;

    /// <summary>Sayıyı ayarlara göre yazar: önek, sıfırla doldurulmuş sayı.</summary>
    public string Format(int number) =>
        Prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(Width, '0');

    /// <summary>Bu ayarların kodunu taşıyan bir tel numarasının sayısı; kodsuz, sayıdan başka bir şey içeren ya da boş numarada <c>false</c>.</summary>
    public bool TryRead(string? number, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(number) || !number.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = number[Prefix.Length..];
        return digits.Length > 0 && digits.All(char.IsAsciiDigit) && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
