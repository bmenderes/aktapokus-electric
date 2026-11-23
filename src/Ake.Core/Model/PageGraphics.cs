using System.Globalization;

namespace Ake.Core.Model;

public readonly record struct GraphicId(Guid Value)
{
    public static GraphicId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Sayfaya çizilen serbest grafik: çizgi, çoklu çizgi, daire ve yazı. Elektriksel anlamı yoktur (cihaz,
/// bağlantı ya da potansiyel oluşturmaz); başka bir araçtan içe aktarılan çizimler (PDF, DXF) ve elle
/// eklenen notlar için kullanılır (ADR-0028). Koordinatlar sayfa koordinatıdır, milimetre, y aşağı doğru.
/// </summary>
public abstract record GraphicShape
{
    /// <summary>Çizgi rengi, <c>#rrggbb</c>; boşsa sayfanın varsayılan rengi (siyah).</summary>
    public string? Color { get; init; }

    /// <summary>Şeklin sayfa üzerindeki sınır kutusu.</summary>
    public abstract (double X, double Y, double Width, double Height) Bounds { get; }

    /// <summary>Şekli verilen mesafe kadar kaydırır.</summary>
    public abstract GraphicShape Translate(double dx, double dy);

    /// <summary>Şeklin geçerliliğini denetler; geçerliyse <c>null</c>.</summary>
    public abstract string? Check();

    protected string? CheckColor() =>
        Color is null || (Color.Length == 7 && Color[0] == '#' && Color[1..].All(Uri.IsHexDigit))
            ? null
            : $"renk #rrggbb biçiminde olmalı: {Color}";

    protected static bool Finite(params double[] numbers) => numbers.All(double.IsFinite);
}

/// <summary>
/// Çoklu çizgi (en az iki nokta); <c>Closed</c> ise son nokta ilkine bağlanır, <c>Filled</c> ise içi
/// çizgi renginde dolar. <c>Points</c> x, y, x, y… sırasındadır.
/// </summary>
public sealed record GraphicPolyline(IReadOnlyList<double> Points, bool Closed = false, double StrokeMm = 0.25, bool Filled = false)
    : GraphicShape
{
    public override (double X, double Y, double Width, double Height) Bounds
    {
        get
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (var i = 0; i + 1 < Points.Count; i += 2)
            {
                (minX, maxX) = (Math.Min(minX, Points[i]), Math.Max(maxX, Points[i]));
                (minY, maxY) = (Math.Min(minY, Points[i + 1]), Math.Max(maxY, Points[i + 1]));
            }

            return Points.Count < 2 ? (0, 0, 0, 0) : (minX, minY, maxX - minX, maxY - minY);
        }
    }

    public override GraphicShape Translate(double dx, double dy) =>
        this with { Points = [.. Points.Select((value, i) => value + (i % 2 == 0 ? dx : dy))] };

    public override string? Check() =>
        Points.Count < 4 || Points.Count % 2 != 0 ? "çoklu çizgi en az iki nokta (x, y çifti) ister"
        : !Points.All(double.IsFinite) || !Finite(StrokeMm) ? "koordinatlar sonlu sayı olmalı"
        : StrokeMm <= 0 ? "çizgi kalınlığı sıfırdan büyük olmalı"
        : CheckColor();

    /// <summary>Noktaları <c>x,y x,y…</c> metnine çevirir (dosya biçimi; üç ondalık).</summary>
    public static string Encode(IReadOnlyList<double> points) => string.Join(
        ' ',
        Enumerable.Range(0, points.Count / 2).Select(i => string.Create(
            CultureInfo.InvariantCulture, $"{Math.Round(points[2 * i], 3) + 0.0:0.###},{Math.Round(points[(2 * i) + 1], 3) + 0.0:0.###}")));

    /// <summary><see cref="Encode"/> çıktısını okur; bozuksa <c>null</c>.</summary>
    public static double[]? Decode(string text)
    {
        var values = new List<double>();
        foreach (var pair in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(',');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                return null;
            }

            values.Add(x);
            values.Add(y);
        }

        return [.. values];
    }
}

public sealed record GraphicCircle(double X, double Y, double Radius, double StrokeMm = 0.25, bool Filled = false)
    : GraphicShape
{
    public override (double X, double Y, double Width, double Height) Bounds => (X - Radius, Y - Radius, 2 * Radius, 2 * Radius);

    public override GraphicShape Translate(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public override string? Check() =>
        !Finite(X, Y, Radius, StrokeMm) ? "koordinatlar sonlu sayı olmalı"
        : Radius <= 0 ? "yarıçap sıfırdan büyük olmalı"
        : StrokeMm <= 0 ? "çizgi kalınlığı sıfırdan büyük olmalı"
        : CheckColor();
}

/// <summary>
/// Yazı; (X, Y) yazının taban çizgisindeki bağlama noktasıdır. Tek satırdır. <c>RotationDeg</c> yazıyı (X, Y)
/// çevresinde saat yönünde döndürür; -90 aşağıdan yukarı okunan dik yazıdır.
/// </summary>
public sealed record GraphicText(
    double X, double Y, string Text, double SizeMm = 2.5, TextAlign Align = TextAlign.Left, double RotationDeg = 0)
    : GraphicShape
{
    // Genişlik yaklaşık: ortalama harf genişliği yazı boyunun yarısı kadar kabul edilir (seçim kutusu için yeter).
    public override (double X, double Y, double Width, double Height) Bounds
    {
        get
        {
            var width = Text.Length * SizeMm * 0.5;
            var (from, to) = Align switch
            {
                TextAlign.Center => (-width / 2, width / 2),
                TextAlign.Right => (-width, 0),
                _ => (0.0, width),
            };

            // Yazının kutusu (yazı düzleminde) döndürülüp sınır kutusu alınır.
            var radians = RotationDeg * Math.PI / 180;
            var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (u, v) in new[] { (from, -SizeMm), (to, -SizeMm), (to, SizeMm * 0.25), (from, SizeMm * 0.25) })
            {
                var (px, py) = (X + (u * cos) - (v * sin), Y + (u * sin) + (v * cos));
                (minX, maxX) = (Math.Min(minX, px), Math.Max(maxX, px));
                (minY, maxY) = (Math.Min(minY, py), Math.Max(maxY, py));
            }

            return (minX, minY, maxX - minX, maxY - minY);
        }
    }

    public override GraphicShape Translate(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public override string? Check() =>
        !Finite(X, Y, SizeMm, RotationDeg) ? "koordinatlar sonlu sayı olmalı"
        : string.IsNullOrWhiteSpace(Text) || Text.Contains('\n', StringComparison.Ordinal) ? "yazı boş olamaz ve tek satır olmalı"
        : SizeMm <= 0 ? "yazı boyu sıfırdan büyük olmalı"
        : CheckColor();
}

/// <summary>Bir sayfadaki serbest grafik: kimliği, sayfası ve şekli. Şekil değişince yenisiyle değiştirilir.</summary>
public sealed class PageGraphic(GraphicId id, PageId pageId, GraphicShape shape)
{
    public GraphicId Id { get; } = id;

    public PageId PageId { get; } = pageId;

    public GraphicShape Shape { get; internal set; } = shape;
}
