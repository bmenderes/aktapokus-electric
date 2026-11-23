namespace Ake.Core.Model;

public readonly record struct SymbolId(Guid Value)
{
    public static SymbolId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

// Sembolün çizim öğeleri. Koordinatlar sembolün kendi sisteminde, milimetredir; başlangıç noktası
// (0, 0) ilk bağlantı ucudur.

public abstract record SymbolShape;

public sealed record SymbolLine(double X1, double Y1, double X2, double Y2, double StrokeMm = 0.25) : SymbolShape;

public sealed record SymbolRect(double X, double Y, double Width, double Height, double StrokeMm = 0.25) : SymbolShape;

public sealed record SymbolCircle(double X, double Y, double Radius, double StrokeMm = 0.25, bool Filled = false)
    : SymbolShape;

public sealed record SymbolText(double X, double Y, string Text, double SizeMm = 2.5, TextAlign Align = TextAlign.Left, double RotationDeg = 0)
    : SymbolShape;

/// <summary>
/// Sembolün bağlantı ucu. Sırası function'ın bağlantı noktası sırasıdır. Yön, telin sembolden
/// uzaklaştığı taraftır: (0, -1) yukarı, (0, 1) aşağı, (-1, 0) sola, (1, 0) sağa.
/// </summary>
public sealed record SymbolPin(
    double X, double Y, int DirectionX, int DirectionY, string? Name = null, PointAttributes Attributes = default);

/// <summary>
/// Kullanıcının tanımladığı (ya da dışarıdan içe aktarılan) sembol. Projenin içinde saklanır; bir
/// function'a atanırsa o function yerleşik parametrik sembol yerine bununla çizilir.
/// </summary>
public sealed record SymbolDefinition(
    SymbolId Id,
    string Name,
    IReadOnlyList<SymbolShape> Shapes,
    IReadOnlyList<SymbolPin> Pins,
    string? Source = null,
    string? License = null,
    string? DeviceName = null,
    string? TagLetter = null)
{
    public const int MaxDeviceNameLength = 120;
    /// <summary>Sembolün çizilebilir olup olmadığını denetler.</summary>
    public Error? Check()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return new Error("sembol.gecersiz", "Sembolün adı boş olamaz.");
        }

        if (Pins.Count == 0)
        {
            return Invalid("en az bir bağlantı ucu olmalı.");
        }

        if (Pins.Any(pin => Math.Abs(pin.DirectionX) + Math.Abs(pin.DirectionY) != 1))
        {
            return Invalid("uç yönü dört ana yönden biri olmalı.");
        }

        if (Pins.Select(pin => (Math.Round(pin.X, 3), Math.Round(pin.Y, 3))).Distinct().Count() != Pins.Count)
        {
            return Invalid("iki uç aynı noktada olamaz.");
        }

        if (Pins.Select(pin => pin.Name).Where(name => name is not null).GroupBy(name => name).Any(group => group.Count() > 1))
        {
            return Invalid("iki ucun adı aynı olamaz.");
        }

        if (Pins.FirstOrDefault(pin => pin.Name is not null && (pin.Name.Length == 0 || pin.Name.Any(char.IsWhiteSpace) || pin.Name.Contains(':', StringComparison.Ordinal))) is { } badName)
        {
            return Invalid($"uç adı boş olamaz, boşluk ya da ':' içeremez: '{badName.Name}'.");
        }

        if (Pins.Select(pin => pin.Attributes.Check()).FirstOrDefault(problem => problem is not null) is { } attributeProblem)
        {
            return Invalid(attributeProblem + ".");
        }

        if (DeviceName is { Length: > MaxDeviceNameLength })
        {
            return Invalid($"cihaz adı en çok {MaxDeviceNameLength} karakter olabilir.");
        }

        if (TagLetter is not null && (TagLetter.Length is < 1 or > 3 || !TagLetter.All(char.IsAsciiLetterUpper)))
        {
            return Invalid("tag harfi 1–3 büyük harf olmalı.");
        }

        var numbers = Shapes.SelectMany(Numbers).Concat(Pins.SelectMany(pin => new[] { pin.X, pin.Y }));
        if (numbers.Any(number => !double.IsFinite(number)))
        {
            return Invalid("koordinatlar sonlu sayı olmalı.");
        }

        var degenerate = Shapes.Any(shape => shape switch
        {
            SymbolRect rect => rect.Width <= 0 || rect.Height <= 0,
            SymbolCircle circle => circle.Radius <= 0,
            SymbolText text => text.SizeMm <= 0 || text.Text.Length == 0,
            _ => false,
        });
        return degenerate ? Invalid("sıfır boyutlu ya da boş bir öğe var.") : null;
    }

    private static IEnumerable<double> Numbers(SymbolShape shape) => shape switch
    {
        SymbolLine line => [line.X1, line.Y1, line.X2, line.Y2, line.StrokeMm],
        SymbolRect rect => [rect.X, rect.Y, rect.Width, rect.Height, rect.StrokeMm],
        SymbolCircle circle => [circle.X, circle.Y, circle.Radius, circle.StrokeMm],
        SymbolText text => [text.X, text.Y, text.SizeMm, text.RotationDeg],
        _ => [],
    };

    private Error Invalid(string detail) => new("sembol.gecersiz", $"'{Name}' sembolü geçersiz: {detail}");
}
