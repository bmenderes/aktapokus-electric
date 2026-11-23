using Ake.Core.Model;

namespace Ake.Render;

/// <summary>Sembolün bir bağlantı ucu: konumu ve telin sembolden uzaklaştığı yön (birim vektör).</summary>
public readonly record struct Pin(double X, double Y, double DirectionX, double DirectionY);

/// <summary>
/// Bir function'ın sembolü, kendi koordinatlarında (mm). Başlangıç noktası ilk bağlantı ucudur; sembol
/// aşağı doğru uzanır. <see cref="Pins"/> sırası function'ın bağlantı noktası sırasıdır.
/// </summary>
/// <remarks><see cref="MinY"/> ve <see cref="Height"/> dikey sınırlardır: sembol MinY ile Height arasında uzanır.</remarks>
public sealed record Symbol(
    IReadOnlyList<SceneItem> Items,
    IReadOnlyList<Pin> Pins,
    double MinX,
    double MaxX,
    double Height = Symbol.Length,
    double MinY = 0)
{
    /// <summary>İki uçlu sembollerin boyu ve çok kutuplu cihazlarda kutuplar arası adım.</summary>
    public const double Length = 15;

    public const double Pitch = 10;
}

/// <summary>
/// Parametrik sembol üretici: semboller elle çizilmez, function'ın türünden ve bağlantı noktası
/// sayısından kurala göre üretilir. Biçimler IEC 60617'nin sadeleştirilmiş halidir.
/// </summary>
public static class SymbolLibrary
{
    /// <summary>
    /// Function'ın sembolü: projede ona atanmış bir kullanıcı sembolü varsa o, yoksa yerleşik parametrik sembol.
    /// </summary>
    public static Symbol For(Function function, Project project)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(project);

        return function.SymbolId is { } id && project.FindSymbol(id) is { } definition
            ? FromDefinition(definition)
            : For(function);
    }

    /// <summary>
    /// Kullanıcı sembolünü çizilecek biçime çevirir. Sınırlar bütün öğeleri ve uçları kapsar; yazıların
    /// genişliği yazı boyundan tahmin edilir.
    /// </summary>
    public static Symbol FromDefinition(SymbolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var items = new List<SceneItem>();
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var shape in definition.Shapes)
        {
            switch (shape)
            {
                case SymbolLine line:
                    items.Add(new LineItem(line.X1, line.Y1, line.X2, line.Y2, line.StrokeMm));
                    xs.AddRange([line.X1, line.X2]);
                    ys.AddRange([line.Y1, line.Y2]);
                    break;
                case SymbolRect rect:
                    items.Add(new RectItem(rect.X, rect.Y, rect.Width, rect.Height, rect.StrokeMm));
                    xs.AddRange([rect.X, rect.X + rect.Width]);
                    ys.AddRange([rect.Y, rect.Y + rect.Height]);
                    break;
                case SymbolCircle circle:
                    items.Add(new CircleItem(circle.X, circle.Y, circle.Radius, circle.StrokeMm, circle.Filled));
                    xs.AddRange([circle.X - circle.Radius, circle.X + circle.Radius]);
                    ys.AddRange([circle.Y - circle.Radius, circle.Y + circle.Radius]);
                    break;
                case SymbolText text:
                    items.Add(new TextItem(text.X, text.Y, text.Text, text.SizeMm, string.Empty, text.Align, text.RotationDeg));
                    var width = text.Text.Length * text.SizeMm * 0.6;
                    var left = text.Align switch
                    {
                        TextAlign.Center => text.X - (width / 2),
                        TextAlign.Right => text.X - width,
                        _ => text.X,
                    };
                    if (Math.Abs(text.RotationDeg) >= 0.01)
                    {
                        var relLeft = left - text.X;
                        var relRight = relLeft + width;
                        var rad = text.RotationDeg * Math.PI / 180;
                        var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
                        foreach (var (u, v) in new[] { (relLeft, -text.SizeMm), (relRight, -text.SizeMm), (relRight, 0.0), (relLeft, 0.0) })
                        {
                            xs.Add(text.X + (u * cos) - (v * sin));
                            ys.Add(text.Y + (u * sin) + (v * cos));
                        }
                    }
                    else
                    {
                        xs.AddRange([left, left + width]);
                        ys.AddRange([text.Y - text.SizeMm, text.Y]);
                    }
                    break;
            }
        }

        var pins = definition.Pins.Select(pin => new Pin(pin.X, pin.Y, pin.DirectionX, pin.DirectionY)).ToList();
        xs.AddRange(pins.Select(pin => pin.X));
        ys.AddRange(pins.Select(pin => pin.Y));

        // Çok ince sembolün de seçilebilir ve tel yolu için engel olacak bir kutusu olsun.
        var (minX, maxX) = Widen(xs.Min(), xs.Max());
        var (minY, maxY) = Widen(ys.Min(), ys.Max());
        return new Symbol(items, pins, minX, maxX, maxY, minY);

        static (double Min, double Max) Widen(double min, double max) =>
            max - min >= 2 ? (min, max) : (((min + max) / 2) - 1, ((min + max) / 2) + 1);
    }

    public static Symbol For(Function function)
    {
        ArgumentNullException.ThrowIfNull(function);

        var points = function.ConnectionPoints.Count;
        if (function.Kind == FunctionKind.Terminal)
        {
            // Klemens: sonlandırıcı (nokta yok), çok bağlantılı klemens ve PE klemensi kendi simgesini alır (ADR-0040).
            if (function.Role == TerminalRole.EndCover)
            {
                return EndCover();
            }

            if (function.Ways > 1)
            {
                return MultiTerminal(function.Ways, function.Role == TerminalRole.ProtectiveEarth);
            }

            if (function.Role == TerminalRole.ProtectiveEarth && points == 2)
            {
                return ProtectiveEarthTerminal();
            }
        }

        return (function.Kind, points) switch
        {
            (FunctionKind.NoContact or FunctionKind.PowerContact, 2) => NormallyOpen(),
            (FunctionKind.NcContact, 2) => NormallyClosed(),
            (FunctionKind.Protection, 2) => Fuse(),
            (FunctionKind.Coil, 2) => Coil(),
            (FunctionKind.Terminal, 2) => Terminal(),
            (FunctionKind.Motor, >= 3 and <= 4) => Motor(points),
            (FunctionKind.PlcChannel, 1) => PlcChannel(),
            (FunctionKind.Junction, 1) => Junction(),
            _ => Box(points),
        };
    }

    // PE klemensi: sıradan klemens simgesi ve yanında "PE".
    private static Symbol ProtectiveEarthTerminal() => TwoPin(
        1,
        new LineItem(0, 0, 0, 6.5),
        new CircleItem(0, 7.5, 1),
        new LineItem(0, 8.5, 0, 15),
        new TextItem(2.4, 8.7, "PE", 2.2, string.Empty));

    // Çok bağlantılı klemens: üstte iç taraf, altta dış taraf noktaları (5 mm adım); ortada bunları birleştiren bara.
    private static Symbol MultiTerminal(int ways, bool earth)
    {
        var items = new List<SceneItem> { new RectItem(-2.5, 6, ((ways - 1) * Symbol.Pitch / 2) + 5, 3) };
        var pins = new List<Pin>();
        for (var i = 0; i < ways; i++)
        {
            var x = i * 5.0;
            items.Add(new LineItem(x, 0, x, 6));
            items.Add(new LineItem(x, 9, x, 15));
            pins.Add(new Pin(x, 0, 0, -1));
        }

        for (var i = 0; i < ways; i++)
        {
            pins.Add(new Pin(i * 5.0, 15, 0, 1));
        }

        if (earth)
        {
            items.Add(new TextItem(((ways - 1) * 5) + 1.5, 9, "PE", 2.2, string.Empty));
        }

        return new Symbol(items, pins, -2.5, ((ways - 1) * 5) + 2.5);
    }

    // Sonlandırıcı: elektriksel bağlantısı yok; şerit sonunu gösteren ince levha.
    private static Symbol EndCover() => new(
        [new RectItem(-1.2, 2, 2.4, 11), new LineItem(-1.2, 2, 1.2, 13)],
        [],
        -1.2,
        1.2);

    private static Symbol TwoPin(double halfWidth, params SceneItem[] items) => new(
        items,
        [new Pin(0, 0, 0, -1), new Pin(0, Symbol.Length, 0, 1)],
        -halfWidth,
        halfWidth);

    private static Symbol NormallyOpen() => TwoPin(
        3,
        new LineItem(0, 0, 0, 5),
        new LineItem(0, 10, -3, 4.5),
        new LineItem(0, 10, 0, 15));

    private static Symbol NormallyClosed() => TwoPin(
        3,
        new LineItem(0, 0, 0, 5),
        new LineItem(0, 5, 2.5, 5),
        new LineItem(0, 10, 2.6, 4.2),
        new LineItem(0, 10, 0, 15));

    private static Symbol Fuse() => TwoPin(
        1.5,
        new LineItem(0, 0, 0, 15),
        new RectItem(-1.5, 4, 3, 7));

    private static Symbol Coil() => TwoPin(
        4,
        new LineItem(0, 0, 0, 4.5),
        new RectItem(-4, 4.5, 8, 6),
        new LineItem(0, 10.5, 0, 15));

    private static Symbol Terminal() => TwoPin(
        1,
        new LineItem(0, 0, 0, 6.5),
        new CircleItem(0, 7.5, 1),
        new LineItem(0, 8.5, 0, 15));

    // Üç faz ucu üstte kutup adımıyla dizilir; böylece üstündeki kontaktörün kutuplarıyla hizalanır.
    private static Symbol Motor(int points)
    {
        var items = new List<SceneItem>
        {
            new CircleItem(10, 14, 7),
            new TextItem(10, 15, "M", 4, string.Empty, TextAlign.Center),
            new TextItem(10, 18.5, "3~", 2.2, string.Empty, TextAlign.Center),
            new LineItem(0, 0, 0, 6),
            new LineItem(0, 6, 5.2, 8.9),
            new LineItem(10, 0, 10, 7),
            new LineItem(20, 0, 20, 6),
            new LineItem(20, 6, 14.8, 8.9),
        };
        var pins = new List<Pin> { new(0, 0, 0, -1), new(10, 0, 0, -1), new(20, 0, 0, -1) };

        if (points == 4)
        {
            items.Add(new LineItem(30, 0, 30, 14));
            items.Add(new LineItem(30, 14, 17, 14));
            pins.Add(new Pin(30, 0, 0, -1));
        }

        return new Symbol(items, pins, -1, points == 4 ? 31 : 21, 21.5);
    }

    // Kavşak: ucun üstünde dolu küçük nokta. Ucun yönü yoktur; teller her yönden gelebilir.
    private static Symbol Junction() => new(
        [new CircleItem(0, 0, 0.9, 0.25, true)], [new Pin(0, 0, 0, 0)], -1, 1, 1, -1);

    // PLC kanalı yalnızca ucunu çizer; kartın gövdesi, yan yana duran kanalların çevresine tek kutu
    // olarak şema çiziminde eklenir.
    private static Symbol PlcChannel() => new(
        [new LineItem(0, 0, 0, 3)], [new Pin(0, 0, 0, -1)], -3, 3);

    // Genel kutu: uçların ilk yarısı üstte, kalanı altta.
    private static Symbol Box(int points)
    {
        var top = (points + 1) / 2;
        var bottom = points - top;
        var columns = Math.Max(Math.Max(top, bottom), 1);
        var items = new List<SceneItem> { new RectItem(-4, 3, ((columns - 1) * Symbol.Pitch) + 8, 9) };
        var pins = new List<Pin>();

        for (var i = 0; i < top; i++)
        {
            items.Add(new LineItem(i * Symbol.Pitch, 0, i * Symbol.Pitch, 3));
            pins.Add(new Pin(i * Symbol.Pitch, 0, 0, -1));
        }

        for (var i = 0; i < bottom; i++)
        {
            items.Add(new LineItem(i * Symbol.Pitch, 12, i * Symbol.Pitch, Symbol.Length));
            pins.Add(new Pin(i * Symbol.Pitch, Symbol.Length, 0, 1));
        }

        return new Symbol(items, pins, -4, ((columns - 1) * Symbol.Pitch) + 4);
    }
}
