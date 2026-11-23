namespace Ake.Core.Model;

public readonly record struct SheetTemplateId(Guid Value)
{
    public static SheetTemplateId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Antetin bir hücresi. Konum, antetin sol üst köşesine göre milimetredir. <see cref="Content"/>
/// içindeki yer tutucular (<c>{project.name}</c>, <c>{page.number}</c>…) çıktı alınırken doldurulur.
/// </summary>
public sealed record TitleBlockCell(
    double XMm,
    double YMm,
    double WidthMm,
    double HeightMm,
    string? Label = null,
    string? Content = null,
    double FontSizeMm = 3,
    TextAlign Align = TextAlign.Left,
    bool Border = true,
    string? ImageRef = null);

/// <summary>
/// Sayfa şablonu: kağıt boyutu, çerçeve, kolon/satır ızgarası ve antet. Kullanıcı kendi şablonunu
/// tanımlar; şablon projenin içinde saklanır, böylece proje dosyası tek başına çıktı verebilir.
/// Bütün ölçüler milimetredir.
/// </summary>
public sealed record SheetTemplate(
    SheetTemplateId Id,
    string Name,
    double WidthMm,
    double HeightMm,
    double MarginMm,
    int Columns,
    int Rows,
    double GridBandMm,
    double TitleBlockHeightMm,
    string FontFamily,
    IReadOnlyList<TitleBlockCell> Cells,
    int FirstColumnNumber = 0)
{
    /// <summary>Şablonun çizilebilir olup olmadığını denetler.</summary>
    public Error? Check()
    {
        if (WidthMm <= 0 || HeightMm <= 0 || MarginMm < 0 || GridBandMm < 0 || TitleBlockHeightMm < 0)
        {
            return Invalid("ölçüler negatif olamaz, kağıt boyutu sıfırdan büyük olmalı.");
        }

        if (Columns < 1 || Rows < 1 || Rows > 26)
        {
            return Invalid("en az bir kolon, 1–26 arası satır olmalı.");
        }

        if (string.IsNullOrWhiteSpace(FontFamily))
        {
            return Invalid("yazı tipi boş olamaz.");
        }

        var innerWidth = WidthMm - (2 * MarginMm);
        var innerHeight = HeightMm - (2 * MarginMm);
        if (innerWidth - (2 * GridBandMm) <= 0 || innerHeight - GridBandMm - TitleBlockHeightMm <= 0)
        {
            return Invalid("kenar boşluğu, ızgara bandı ve antetten sonra çizim alanı kalmıyor.");
        }

        foreach (var cell in Cells)
        {
            if (cell.WidthMm <= 0 || cell.HeightMm <= 0 || cell.FontSizeMm <= 0
                || cell.XMm < 0 || cell.YMm < 0
                || cell.XMm + cell.WidthMm > innerWidth + 0.001
                || cell.YMm + cell.HeightMm > TitleBlockHeightMm + 0.001)
            {
                return Invalid($"'{cell.Label ?? cell.Content ?? cell.ImageRef}' hücresi antetin dışına taşıyor ya da ölçüsü geçersiz.");
            }
        }

        return null;
    }

    private Error Invalid(string detail) => new("sablon.gecersiz", $"'{Name}' sayfa şablonu geçersiz: {detail}");
}
