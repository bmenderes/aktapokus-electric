using Ake.Core;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

public enum SymbolTool
{
    Select,
    Line,
    Rect,
    Circle,
    Text,
    Pin,
}

/// <summary>Taslaktaki bir öğe: çizim öğesi ya da bağlantı ucu.</summary>
public readonly record struct DraftItem(bool IsPin, int Index);

/// <summary>
/// Blok editörünün taslağı: üzerinde çalışılan sembol, araçlar, seçim ve geri alma. Arayüz
/// kütüphanesine bağlı değildir. Taslak projeye ancak <see cref="ToDefinition"/> ile alınıp
/// <c>PutSymbol</c> command'ıyla kaydedilince girer; o zamana kadar proje değişmez.
/// </summary>
/// <remarks>
/// Koordinatlar sembolün kendi sisteminde, milimetredir; başlangıç noktası ilk bağlantı ucudur ve
/// ekranda artı işaretiyle gösterilir. Bütün noktalar <see cref="GridMm"/> adımına yapışır.
/// Geri alma taslağın anlık görüntüsüyle yapılır: sembol küçüktür ve bu henüz modelde değildir.
/// </remarks>
public sealed class SymbolDraft
{
    /// <summary>Sembol ızgarası (mm). Uçlar bu adımda durur; sayfadaki 5 mm ızgarayla da uyumludur.</summary>
    public const double GridMm = 2.5;

    /// <summary>Çalışma alanının yarı genişliği ve yarı yüksekliği (mm); başlangıç noktası ortadadır.</summary>
    public const double HalfWidthMm = 40;

    public const double HalfHeightMm = 30;

    private const double CanvasMarginMm = 10.0;
    private const double CanvasStepMm = 5.0;
    private const double HitToleranceMm = 1.2;
    private const double TextHitToleranceMm = 0.8;

    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private List<SymbolShape> _shapes;
    private List<SymbolPin> _pins;

    public SymbolDraft(SymbolDefinition? start = null)
    {
        Id = start?.Id ?? SymbolId.New();
        Name = start?.Name ?? "Yeni sembol";
        Source = start?.Source ?? "Blok editörü";
        License = start?.License;
        DeviceName = start?.DeviceName;
        TagLetter = start?.TagLetter;
        _shapes = [.. start?.Shapes ?? []];
        _pins = [.. start?.Pins ?? []];
    }

    public SymbolId Id { get; private set; }

    /// <summary>Kütüphaneye kaydedilince sembol yeni kimlik alabilir (asıl sembolün kopyası); sonraki kayıtlar onu günceller.</summary>
    public void AdoptId(SymbolId id) => Id = id;

    /// <summary>Çizim ızgarası (mm): 5, 2,5 (varsayılan), 1,25 ya da 0,5. Yuvarlak şekillere uç koyarken küçük adım gerekir.</summary>
    public double Grid { get; set; } = GridMm;

    /// <summary>Değeri bu taslağın ızgara adımına yuvarlar.</summary>
    public double Round(double value) => (Math.Round(value / Grid) * Grid) + 0.0;

    public string Name { get; set; }

    public string? Source { get; set; }

    public string? License { get; set; }

    /// <summary>Sembolden yeni cihaz oluşturulunca cihazın açıklaması (ADR-0033); boşsa sembol adı.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Sembolden yeni cihaz oluşturulunca tag harfi (1–3 büyük harf); boşsa sembol adından çıkarılır.</summary>
    public string? TagLetter { get; set; }

    public SymbolTool Tool { get; set; } = SymbolTool.Line;

    /// <summary>Yeni uç eklenirken telin çıkacağı yön.</summary>
    public (int X, int Y) PinDirection { get; set; } = (0, -1);

    /// <summary>Yazı aracıyla eklenecek metin.</summary>
    public string Text { get; set; } = "A";

    public IReadOnlyList<SymbolShape> Shapes => _shapes;

    public IReadOnlyList<SymbolPin> Pins => _pins;

    public DraftItem? Selected { get; private set; }

    /// <summary>Seçili uç; seçim uç değilse <c>null</c>.</summary>
    public SymbolPin? SelectedPin => Selected is { IsPin: true } item && item.Index < _pins.Count ? _pins[item.Index] : null;

    /// <summary>Seçili yazı; seçim yazı değilse <c>null</c>.</summary>
    public SymbolText? SelectedText =>
        Selected is { IsPin: false } item && item.Index < _shapes.Count && _shapes[item.Index] is SymbolText text
            ? text
            : null;

    /// <summary>Çalışma alanının sahne koordinatlarındaki başlangıç noktası X (mm).</summary>
    public double SceneOriginX => CanvasLayout().OriginX;

    /// <summary>Çalışma alanının sahne koordinatlarındaki başlangıç noktası Y (mm).</summary>
    public double SceneOriginY => CanvasLayout().OriginY;

    /// <summary>Çalışma alanının toplam genişliği (mm); büyük sembollere göre büyür.</summary>
    public double SceneWidthMm => CanvasLayout().Width;

    /// <summary>Çalışma alanının toplam yüksekliği (mm); büyük sembollere göre büyür.</summary>
    public double SceneHeightMm => CanvasLayout().Height;

    /// <summary>Seçili yazının metnini, boyutunu, hizasını ve açısını günceller. Geri alınır.</summary>
    public Result SetSelectedText(string? text, double sizeMm, TextAlign align, double rotationDeg)
    {
        if (Selected is not { IsPin: false } item || item.Index >= _shapes.Count || _shapes[item.Index] is not SymbolText current)
        {
            return Result.Success();
        }

        var clean = text?.Trim() ?? string.Empty;
        if (clean.Length == 0)
        {
            return Result.Failure(new Error("sembol.yazi_bos", "Yazı metni boş olamaz."));
        }

        if (double.IsNaN(sizeMm) || double.IsInfinity(sizeMm) || sizeMm <= 0 || sizeMm > 100)
        {
            return Result.Failure(new Error("sembol.yazi_boyutu", "Yazı boyutu 0 ile 100 mm arasında olmalı."));
        }

        if (double.IsNaN(rotationDeg) || double.IsInfinity(rotationDeg))
        {
            return Result.Failure(new Error("sembol.yazi_acisi", "Yazı açısı geçerli bir sayı olmalı."));
        }

        var updated = current with { Text = clean, SizeMm = sizeMm, Align = align, RotationDeg = rotationDeg };
        if (updated == current)
        {
            return Result.Success();
        }

        Record();
        _shapes[item.Index] = updated;
        return Result.Success();
    }

    /// <summary>
    /// Seçili ucun adını ve özelliklerini değiştirir (ADR-0033). Boş ad "adsız" demektir (uç sırası numarasıyla anılır). Geri
    /// alınır.
    /// </summary>
    public Result SetSelectedPin(string? name, PointAttributes attributes)
    {
        if (Selected is not { IsPin: true } item)
        {
            return Result.Success();
        }

        var cleanName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var cleanCode = string.IsNullOrWhiteSpace(attributes.ConnectorCode) ? null : attributes.ConnectorCode.Trim();
        var updated = _pins[item.Index] with { Name = cleanName, Attributes = attributes with { ConnectorCode = cleanCode } };
        if (updated == _pins[item.Index])
        {
            return Result.Success();
        }

        if (cleanName is not null && _pins.Where((_, i) => i != item.Index).Any(other => other.Name == cleanName))
        {
            return Result.Failure(new Error("sembol.uc_ad_tekrar", $"'{cleanName}' adlı başka bir uç var."));
        }

        Record();
        _pins[item.Index] = updated;
        return Result.Success();
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Kaydedilmemiş değişiklik var mı.</summary>
    public bool IsDirty { get; private set; }

    // "+ 0.0", -0 değerini 0 yapar; yoksa dosyaya "-0" yazılır ve karşılaştırmalar şaşırır.
    public static double Snap(double value) => (Math.Round(value / GridMm) * GridMm) + 0.0;

    /// <summary>
    /// Bir araç hareketini uygular: basılan noktadan bırakılan noktaya. Seçim aracında öğe taşınır;
    /// çizim araçlarında yeni öğe eklenir. Boyutsuz bir çizim (aynı noktaya bırakma) bir şey eklemez.
    /// </summary>
    public Result Gesture(double fromX, double fromY, double toX, double toY)
    {
        var (x1, y1) = (Round(fromX), Round(fromY));
        var (x2, y2) = (Round(toX), Round(toY));

        switch (Tool)
        {
            case SymbolTool.Select:
                return Selected is { } item ? Move(item, x2 - x1, y2 - y1) : Result.Success();

            case SymbolTool.Line when (x1, y1) != (x2, y2):
                return Add(new SymbolLine(x1, y1, x2, y2));

            case SymbolTool.Rect when x1 != x2 && y1 != y2:
                return Add(new SymbolRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1)));

            case SymbolTool.Circle when (x1, y1) != (x2, y2):
                var radius = Math.Max(Grid, Round(Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)))));
                return Add(new SymbolCircle(x1, y1, radius));

            case SymbolTool.Text when !string.IsNullOrWhiteSpace(Text):
                return Add(new SymbolText(x2, y2, Text.Trim()));

            case SymbolTool.Pin:
                // Çizgi, daire ya da dikdörtgenin kenarına 2 mm'den yakın bırakılan uç kenara oturur (yuvarlak şekle ızgara noktasıyla
                // tam değmek mümkün olmaz); daire üzerindeyse tel merkezden dışarı doğru çıkar.
                var (px, py, direction) = OnOutline(toX, toY, x2, y2);
                if (_pins.Any(pin => Math.Abs(pin.X - px) < 0.001 && Math.Abs(pin.Y - py) < 0.001))
                {
                    return Result.Failure(new Error("sembol.uc_cakisma", "Bu noktada zaten bir uç var."));
                }

                Record();
                _pins.Add(new SymbolPin(px, py, direction?.X ?? PinDirection.X, direction?.Y ?? PinDirection.Y));
                Selected = new DraftItem(true, _pins.Count - 1);
                return Result.Success();

            default:
                return Result.Success();
        }
    }

    /// <summary>Verilen noktadaki öğeyi seçer; önce uçlar, sonra yazılar, ardından son çizilen şekil kazanır.</summary>
    public DraftItem? SelectAt(double x, double y)
    {
        Selected = null;

        int? bestPinIndex = null;
        var bestPinDist = double.MaxValue;
        for (var i = _pins.Count - 1; i >= 0; i--)
        {
            var dx = _pins[i].X - x;
            var dy = _pins[i].Y - y;
            if (Math.Abs(dx) <= HitToleranceMm && Math.Abs(dy) <= HitToleranceMm)
            {
                var dist = (dx * dx) + (dy * dy);
                if (bestPinIndex is null || dist < bestPinDist - 1e-9)
                {
                    bestPinIndex = i;
                    bestPinDist = dist;
                }
            }
        }

        if (bestPinIndex is { } pinIndex)
        {
            return Selected = new DraftItem(true, pinIndex);
        }

        int? bestTextIndex = null;
        var bestTextBoxDist = double.MaxValue;
        var bestTextCenterDist = double.MaxValue;
        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            if (_shapes[i] is not SymbolText text)
            {
                continue;
            }

            var (distToBox, distToCenter) = TextHitMetrics(text, x, y);
            if (distToBox > TextHitToleranceMm)
            {
                continue;
            }

            if (bestTextIndex is null
                || distToBox < bestTextBoxDist - 1e-6
                || (Math.Abs(distToBox - bestTextBoxDist) <= 1e-6 && distToCenter < bestTextCenterDist - 1e-6))
            {
                bestTextIndex = i;
                bestTextBoxDist = distToBox;
                bestTextCenterDist = distToCenter;
            }
        }

        if (bestTextIndex is { } textIndex)
        {
            return Selected = new DraftItem(false, textIndex);
        }

        for (var i = _shapes.Count - 1; i >= 0; i--)
        {
            if (_shapes[i] is not SymbolText && Hits(_shapes[i], x, y))
            {
                return Selected = new DraftItem(false, i);
            }
        }

        return null;
    }

    public void ClearSelection() => Selected = null;

    /// <summary>Seçili öğe uç eklenebilecek bir yüzey (daire ya da dikdörtgen) mi.</summary>
    public bool SelectedIsSurface => Selected is { IsPin: false } item && item.Index < _shapes.Count && _shapes[item.Index] is SymbolCircle or SymbolRect;

    /// <summary>
    /// Seçili daire ya da dikdörtgenin çevresine <paramref name="count"/> uç ekler, eşit aralıklarla (dairede üstten saat yönünde,
    /// dikdörtgende üst kenarın başından çevre boyunca). Teller yüzeyden dışarı doğru çıkar. Aynı noktada uç varsa o nokta atlanır.
    /// Tek adımda geri alınır.
    /// </summary>
    public Result AddPinsOnSelectedSurface(int count)
    {
        if (!SelectedIsSurface)
        {
            return Result.Failure(new Error("sembol.yuzey_degil", "Önce uç eklenecek daire ya da dikdörtgeni seçin."));
        }

        if (count is < 1 or > 64)
        {
            return Result.Failure(new Error("sembol.uc_sayisi", "Uç sayısı 1 ile 64 arasında olmalı."));
        }

        var points = _shapes[Selected!.Value.Index] switch
        {
            SymbolCircle circle => CirclePins(circle, count),
            SymbolRect rect => RectPins(rect, count),
            _ => [],
        };

        Record();
        foreach (var (x, y, dx, dy) in points)
        {
            if (!_pins.Any(pin => Math.Abs(pin.X - x) < 0.01 && Math.Abs(pin.Y - y) < 0.01))
            {
                _pins.Add(new SymbolPin(x, y, dx, dy));
            }
        }

        Selected = null;
        return Result.Success();
    }

    private static List<(double X, double Y, int Dx, int Dy)> CirclePins(SymbolCircle circle, int count)
    {
        var points = new List<(double, double, int, int)>();
        for (var i = 0; i < count; i++)
        {
            var angle = (-Math.PI / 2) + (2 * Math.PI * i / count);
            var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
            var direction = Math.Abs(cos) >= Math.Abs(sin) + 1e-9 ? (Math.Sign(cos), 0) : (0, Math.Sign(sin));
            points.Add((Math.Round(circle.X + (circle.Radius * cos), 2), Math.Round(circle.Y + (circle.Radius * sin), 2), direction.Item1, direction.Item2));
        }

        return points;
    }

    private static List<(double X, double Y, int Dx, int Dy)> RectPins(SymbolRect rect, int count)
    {
        var perimeter = 2 * (rect.Width + rect.Height);
        var step = perimeter / count;
        var points = new List<(double, double, int, int)>();
        for (var i = 0; i < count; i++)
        {
            var d = (step / 2) + (i * step);
            if (d <= rect.Width)
            {
                points.Add((Math.Round(rect.X + d, 2), rect.Y, 0, -1));
            }
            else if (d <= rect.Width + rect.Height)
            {
                points.Add((rect.X + rect.Width, Math.Round(rect.Y + (d - rect.Width), 2), 1, 0));
            }
            else if (d <= (2 * rect.Width) + rect.Height)
            {
                points.Add((Math.Round(rect.X + rect.Width - (d - rect.Width - rect.Height), 2), rect.Y + rect.Height, 0, 1));
            }
            else
            {
                points.Add((rect.X, Math.Round(rect.Y + rect.Height - (d - (2 * rect.Width) - rect.Height), 2), -1, 0));
            }
        }

        return points;
    }

    // Bırakılan noktaya (ham ve ızgaraya yuvarlanmış) en yakın şekil kenarı; ızgara noktası zaten kenardaysa olduğu gibi kalır.
    private (double X, double Y, (int X, int Y)? Direction) OnOutline(double rawX, double rawY, double gridX, double gridY)
    {
        const double snapDistance = 2;
        (double X, double Y, (int X, int Y)? Direction)? best = null;
        var bestDistance = snapDistance;
        foreach (var shape in _shapes)
        {
            switch (shape)
            {
                case SymbolCircle circle:
                    var (dx, dy) = (rawX - circle.X, rawY - circle.Y);
                    var length = Math.Sqrt((dx * dx) + (dy * dy));
                    if (length > 0.001 && Math.Abs(length - circle.Radius) < bestDistance)
                    {
                        bestDistance = Math.Abs(length - circle.Radius);
                        var onX = Math.Round(circle.X + (dx / length * circle.Radius), 2);
                        var onY = Math.Round(circle.Y + (dy / length * circle.Radius), 2);
                        best = (onX, onY, Math.Abs(dx) >= Math.Abs(dy) ? (Math.Sign(dx), 0) : (0, Math.Sign(dy)));
                    }

                    break;
                case SymbolLine line:
                    var (nx, ny, distance) = Nearest(line.X1, line.Y1, line.X2, line.Y2, rawX, rawY);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = (Math.Round(nx, 2), Math.Round(ny, 2), null);
                    }

                    break;
            }
        }

        if (best is null)
        {
            return (gridX, gridY, null);
        }

        // Izgara noktası zaten kenardaysa ızgarada kalmak tercih edilir.
        var gridDistance = _shapes.Select(shape => shape switch
        {
            SymbolCircle c => Math.Abs(Math.Sqrt(((gridX - c.X) * (gridX - c.X)) + ((gridY - c.Y) * (gridY - c.Y))) - c.Radius),
            SymbolLine l => Nearest(l.X1, l.Y1, l.X2, l.Y2, gridX, gridY).Distance,
            _ => double.MaxValue,
        }).DefaultIfEmpty(double.MaxValue).Min();
        return gridDistance < 0.05 ? (gridX, gridY, best.Value.Direction) : best.Value;
    }

    private static (double X, double Y, double Distance) Nearest(double ax, double ay, double bx, double by, double px, double py)
    {
        var (dx, dy) = (bx - ax, by - ay);
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lengthSquared, 0, 1);
        var (cx, cy) = (ax + (t * dx), ay + (t * dy));
        return (cx, cy, Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy))));
    }

    public Result DeleteSelected()
    {
        if (Selected is not { } item)
        {
            return Result.Success();
        }

        Record();
        if (item.IsPin)
        {
            _pins.RemoveAt(item.Index);
        }
        else
        {
            _shapes.RemoveAt(item.Index);
        }

        Selected = null;
        return Result.Success();
    }

    /// <summary>Seçili ucun yönünü saat yönünde 90 derece çevirir.</summary>
    public Result TurnSelectedPin()
    {
        if (Selected is not { IsPin: true } item)
        {
            return Result.Success();
        }

        Record();
        var pin = _pins[item.Index];
        _pins[item.Index] = pin with { DirectionX = -pin.DirectionY, DirectionY = pin.DirectionX };
        return Result.Success();
    }

    /// <summary>Seçili ucu ilk uç yapar (başlangıç noktası); sembol bu uca göre yerleştirilir.</summary>
    public Result MakeSelectedPinFirst()
    {
        if (Selected is not { IsPin: true } item || item.Index == 0)
        {
            return Result.Success();
        }

        Record();
        var pin = _pins[item.Index];
        _pins.RemoveAt(item.Index);
        _pins.Insert(0, pin);
        Selected = new DraftItem(true, 0);
        return Result.Success();
    }

    public void Undo() => Step(_undo, _redo);

    public void Redo() => Step(_redo, _undo);

    /// <summary>
    /// Taslağı sembol tanımına çevirir. Koordinatlar ilk uca göre kaydırılır: ilk uç (0, 0) olur, çünkü
    /// yerleşim ve tel yolu başlangıç noktasını ilk uç kabul eder.
    /// </summary>
    public Result<SymbolDefinition> ToDefinition()
    {
        var (ox, oy) = _pins.Count > 0 ? (_pins[0].X, _pins[0].Y) : (0, 0);
        var definition = new SymbolDefinition(
            Id,
            Name.Trim(),
            [.. _shapes.Select(shape => Shift(shape, -ox, -oy))],
            [.. _pins.Select(pin => pin with { X = pin.X - ox, Y = pin.Y - oy })],
            string.IsNullOrWhiteSpace(Source) ? null : Source.Trim(),
            string.IsNullOrWhiteSpace(License) ? null : License.Trim(),
            string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName.Trim(),
            string.IsNullOrWhiteSpace(TagLetter) ? null : TagLetter.Trim().ToUpperInvariant());

        return definition.Check() is { } error
            ? Result.Failure<SymbolDefinition>(error)
            : Result.Success(definition);
    }

    /// <summary>Kaydedildikten sonra çağrılır; taslak artık temizdir.</summary>
    public void MarkSaved() => IsDirty = false;

    /// <summary>Taslağın ekranda gösterilen sahnesi: çalışma alanı, başlangıç işareti, öğeler ve uçlar.</summary>
    public Scene ToScene()
    {
        var (cx, cy, width, height) = CanvasLayout();
        var items = new List<SceneItem>
        {
            new LineItem(cx - 2, cy, cx + 2, cy, 0.15, "#d62828"),
            new LineItem(cx, cy - 2, cx, cy + 2, 0.15, "#d62828"),
        };

        var symbol = SymbolLibrary.FromDefinition(new SymbolDefinition(
            Id, "taslak", _shapes, _pins.Count > 0 ? _pins : [new SymbolPin(0, 0, 0, -1)]));
        foreach (var item in symbol.Items)
        {
            items.Add(Offset(item, cx, cy));
        }

        for (var i = 0; i < _pins.Count; i++)
        {
            var pin = _pins[i];
            var (px, py) = (cx + pin.X, cy + pin.Y);
            // Uç, bağlantı tipinin kendi simgesiyle gösterilir (tip yoksa içi boş halka); ilk uç (başlangıç noktası) bir dış halkayla ayrılır.
            items.AddRange(ConnectorIcons.Items(pin.Attributes.Connector, px, py));
            if (i == 0)
            {
                items.Add(new CircleItem(px, py, ConnectorIcons.RadiusOf(pin.Attributes.Connector) + 0.6, 0.2, Filled: false));
            }
            items.Add(new LineItem(px, py, px + (pin.DirectionX * 2.5), py + (pin.DirectionY * 2.5), 0.3, "#C77800"));
            var label = pin.Name ?? (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            label += pin.Attributes.Contact switch
            {
                ContactType.NormallyOpen => " NO",
                ContactType.NormallyClosed => " NC",
                ContactType.Changeover => " CO",
                _ => string.Empty,
            };
            var tag = ConnectorIcons.ShortName(pin.Attributes.Connector);
            items.Add(new TextItem(px + ConnectorIcons.RadiusOf(pin.Attributes.Connector) + 1, py - 0.9, tag.Length == 0 ? label : $"{label}  {tag}", 1.6, "Arial"));
        }

        return new Scene(width, height, items);
    }

    /// <summary>Seçili öğenin kutusu (sahne koordinatları), vurgu için.</summary>
    public Box? SelectedBox()
    {
        if (Selected is not { } item)
        {
            return null;
        }

        var (cx, cy, _, _) = CanvasLayout();
        if (item.IsPin)
        {
            var pin = _pins[item.Index];
            return new Box(cx + pin.X - 1, cy + pin.Y - 1, 2, 2);
        }

        var bounds = BoundsOf(_shapes[item.Index]);
        return new Box(cx + bounds.X, cy + bounds.Y, bounds.Width, bounds.Height);
    }

    private (double OriginX, double OriginY, double Width, double Height) CanvasLayout()
    {
        var minX = 0.0;
        var maxX = 0.0;
        var minY = 0.0;
        var maxY = 0.0;

        foreach (var pin in _pins)
        {
            minX = Math.Min(minX, pin.X - 4.0);
            maxX = Math.Max(maxX, pin.X + 8.0);
            minY = Math.Min(minY, pin.Y - 4.0);
            maxY = Math.Max(maxY, pin.Y + 4.0);
        }

        foreach (var shape in _shapes)
        {
            var bounds = BoundsOf(shape);
            minX = Math.Min(minX, bounds.X);
            maxX = Math.Max(maxX, bounds.Right);
            minY = Math.Min(minY, bounds.Y);
            maxY = Math.Max(maxY, bounds.Bottom);
        }

        var originX = Math.Max(HalfWidthMm, CeilStep(Math.Max(0.0, -minX) + CanvasMarginMm));
        var originY = Math.Max(HalfHeightMm, CeilStep(Math.Max(0.0, -minY) + CanvasMarginMm));
        var right = Math.Max(HalfWidthMm, CeilStep(Math.Max(0.0, maxX) + CanvasMarginMm));
        var bottom = Math.Max(HalfHeightMm, CeilStep(Math.Max(0.0, maxY) + CanvasMarginMm));
        return (originX, originY, originX + right, originY + bottom);

        static double CeilStep(double value) => Math.Ceiling(value / CanvasStepMm) * CanvasStepMm;
    }

    private Result Add(SymbolShape shape)
    {
        Record();
        _shapes.Add(shape);
        Selected = new DraftItem(false, _shapes.Count - 1);
        return Result.Success();
    }

    private Result Move(DraftItem item, double dx, double dy)
    {
        if (dx == 0 && dy == 0)
        {
            return Result.Success();
        }

        Record();
        if (item.IsPin)
        {
            var pin = _pins[item.Index];
            _pins[item.Index] = pin with { X = pin.X + dx, Y = pin.Y + dy };
        }
        else
        {
            _shapes[item.Index] = Shift(_shapes[item.Index], dx, dy);
        }

        return Result.Success();
    }

    private void Record()
    {
        _undo.Push(new Snapshot([.. _shapes], [.. _pins]));
        _redo.Clear();
        IsDirty = true;
    }

    private void Step(Stack<Snapshot> from, Stack<Snapshot> to)
    {
        if (!from.TryPop(out var snapshot))
        {
            return;
        }

        to.Push(new Snapshot([.. _shapes], [.. _pins]));
        _shapes = [.. snapshot.Shapes];
        _pins = [.. snapshot.Pins];
        Selected = null;
        IsDirty = true;
    }

    private static SymbolShape Shift(SymbolShape shape, double dx, double dy) => shape switch
    {
        SymbolLine line => line with { X1 = line.X1 + dx, Y1 = line.Y1 + dy, X2 = line.X2 + dx, Y2 = line.Y2 + dy },
        SymbolRect rect => rect with { X = rect.X + dx, Y = rect.Y + dy },
        SymbolCircle circle => circle with { X = circle.X + dx, Y = circle.Y + dy },
        SymbolText text => text with { X = text.X + dx, Y = text.Y + dy },
        _ => shape,
    };

    private static SceneItem Offset(SceneItem item, double dx, double dy) => item switch
    {
        LineItem line => line with { X1 = line.X1 + dx, Y1 = line.Y1 + dy, X2 = line.X2 + dx, Y2 = line.Y2 + dy },
        RectItem rect => rect with { X = rect.X + dx, Y = rect.Y + dy },
        CircleItem circle => circle with { X = circle.X + dx, Y = circle.Y + dy },
        TextItem text => text with { X = text.X + dx, Y = text.Y + dy, FontFamily = "Arial" },
        _ => item,
    };

    private static Box BoundsOf(SymbolShape shape) => shape switch
    {
        SymbolLine line => Pad(new Box(
            Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1))),
        SymbolRect rect => new Box(rect.X, rect.Y, rect.Width, rect.Height),
        SymbolCircle circle => new Box(circle.X - circle.Radius, circle.Y - circle.Radius, 2 * circle.Radius, 2 * circle.Radius),
        SymbolText text => TextBox(text),
        _ => new Box(0, 0, 0, 0),
    };

    private static Box Pad(Box box) =>
        new(box.X - 0.5, box.Y - 0.5, box.Width + 1, box.Height + 1);

    private static (double Left, double Top, double Right, double Bottom) LocalTextRect(SymbolText text)
    {
        var lines = text.Text.Split('\n');
        var maxChars = Math.Max(1, lines.Max(line => line.Length));
        var width = Math.Max(text.SizeMm * 0.6, maxChars * text.SizeMm * 0.6);
        var relLeft = text.Align switch
        {
            TextAlign.Center => -(width / 2),
            TextAlign.Right => -width,
            _ => 0.0,
        };
        var relRight = relLeft + width;
        var relTop = -text.SizeMm;
        var relBottom = ((lines.Length - 1) * text.SizeMm * 1.25) + (text.SizeMm * 0.25);
        return (relLeft, relTop, relRight, relBottom);
    }

    private static Box TextBox(SymbolText text)
    {
        var (relLeft, relTop, relRight, relBottom) = LocalTextRect(text);
        if (Math.Abs(text.RotationDeg) < 0.01)
        {
            return new Box(text.X + relLeft, text.Y + relTop, relRight - relLeft, relBottom - relTop);
        }

        var rad = text.RotationDeg * Math.PI / 180.0;
        var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var minY = double.PositiveInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var (u, v) in new[] { (relLeft, relTop), (relRight, relTop), (relRight, relBottom), (relLeft, relBottom) })
        {
            var wx = text.X + (u * cos) - (v * sin);
            var wy = text.Y + (u * sin) + (v * cos);
            minX = Math.Min(minX, wx);
            maxX = Math.Max(maxX, wx);
            minY = Math.Min(minY, wy);
            maxY = Math.Max(maxY, wy);
        }

        return new Box(minX, minY, maxX - minX, maxY - minY);
    }

    private static (double DistToBox, double DistToCenter) TextHitMetrics(SymbolText text, double px, double py)
    {
        var dx = px - text.X;
        var dy = py - text.Y;
        double u;
        double v;
        if (Math.Abs(text.RotationDeg) < 0.01)
        {
            u = dx;
            v = dy;
        }
        else
        {
            var rad = text.RotationDeg * Math.PI / 180.0;
            var (cos, sin) = (Math.Cos(rad), Math.Sin(rad));
            u = (dx * cos) + (dy * sin);
            v = (-dx * sin) + (dy * cos);
        }

        var (relLeft, relTop, relRight, relBottom) = LocalTextRect(text);
        var du = u < relLeft ? relLeft - u : (u > relRight ? u - relRight : 0.0);
        var dv = v < relTop ? relTop - v : (v > relBottom ? v - relBottom : 0.0);
        var distToBox = Math.Sqrt((du * du) + (dv * dv));

        var midU = (relLeft + relRight) / 2.0;
        var midV = (relTop + relBottom) / 2.0;
        var distToCenter = Math.Sqrt(((u - midU) * (u - midU)) + ((v - midV) * (v - midV)));
        return (distToBox, distToCenter);
    }

    private static bool Hits(SymbolShape shape, double x, double y)
    {
        const double t = HitToleranceMm;
        switch (shape)
        {
            case SymbolLine line:
                return DistanceToSegment(x, y, line.X1, line.Y1, line.X2, line.Y2) <= t;
            case SymbolRect rect:
                var inside = x >= rect.X - t && x <= rect.X + rect.Width + t && y >= rect.Y - t && y <= rect.Y + rect.Height + t;
                var deep = x > rect.X + t && x < rect.X + rect.Width - t && y > rect.Y + t && y < rect.Y + rect.Height - t;
                return inside && !deep;
            case SymbolCircle circle:
                var distance = Math.Sqrt(((x - circle.X) * (x - circle.X)) + ((y - circle.Y) * (y - circle.Y)));
                return Math.Abs(distance - circle.Radius) <= t;
            case SymbolText text:
                return TextHitMetrics(text, x, y).DistToBox <= TextHitToleranceMm;
            default:
                return false;
        }
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var (dx, dy) = (bx - ax, by - ay);
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lengthSquared, 0, 1);
        var (cx, cy) = (ax + (t * dx), ay + (t * dy));
        return Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
    }

    private sealed record Snapshot(List<SymbolShape> Shapes, List<SymbolPin> Pins);
}
