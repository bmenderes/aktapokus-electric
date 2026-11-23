using Ake.Core;
using Ake.Core.Analysis;
using Ake.Core.Model;

namespace Ake.Render;

/// <summary>
/// Tek hat şeması (ADR-0039): beslemeden korumaya, anahtarlamaya ve yüke inen güç zincirinden (<see cref="PowerChain"/>) otomatik çıkar.
/// Her eleman bir kutudur (sembol, tag, açıklama, anma akımı); kol başına bir yük; ortak parçalar (ana şalter) tek kez çizilir. Kenarlarda
/// telin kesiti ya da kablo adı ve tipi yazar. Çerçeve ve antet şablondan gelir, içerik çizim alanına sığdırılır; proje değişmez.
/// </summary>
public static class SingleLineDiagram
{
    private const double BoxWidth = 58;
    private const double BoxHeight = 28;
    private const double ColumnGap = 10;
    private const double LevelGap = 26;
    private const double RootGap = 16;
    private const string FontFamily = "Arial";

    /// <summary>Projede çizilecek bir güç zinciri var mı.</summary>
    public static bool HasChain(Project project) => PowerChain.Build(project ?? throw new ArgumentNullException(nameof(project))).Count > 0;

    /// <summary>Tek hat şeması tek sayfa olarak, projenin şablonunun çerçevesi ve anteti ile; zincir ya da şablon yoksa hata.</summary>
    public static Result<Scene> Build(Project project, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.SheetTemplates.FirstOrDefault() is not { } template)
        {
            return Result.Failure<Scene>(new Error(
                "render.sablon_yok", "Projede sayfa şablonu yok; tek hat şemasının çerçevesi ve anteti için bir şablon ekleyin (ake template set)."));
        }

        if (!HasChain(project))
        {
            return Result.Failure<Scene>(new Error(NoChainCode, NoChainMessage));
        }

        var page = new Page(PageId.New(), "1", "Tek hat şeması", templateId: template.Id, kind: PageKind.SingleLineDiagram);
        return SheetRenderer.Render(project, page, today ?? DateOnly.FromDateTime(DateTime.Now));
    }

    internal const string NoChainCode = "tekhat.zincir_yok";

    internal const string NoChainMessage =
        "Güç zinciri bulunamadı: beslemeye (klemense) faz potansiyeli (L1…) işareti koyun ve koruma, kontaktör ile yükü teller bağlayın.";

    /// <summary>Sayfa içeriği: zinciri verilen çizim alanına (çerçeve ve antetin içi) sığdırarak üretir.</summary>
    internal static void AddTo(List<SceneItem> items, List<string> warnings, Project project, Box area)
    {
        var roots = PowerChain.Build(project);
        if (roots.Count == 0)
        {
            warnings.Add(NoChainMessage);
            items.Add(new TextItem(area.X + (area.Width / 2), area.Y + (area.Height / 2), NoChainMessage, 3, FontFamily, TextAlign.Center));
            return;
        }

        var drawn = new List<SceneItem>();
        var left = 0.0;
        var depth = 0;
        foreach (var root in roots)
        {
            var (width, levels) = Draw(drawn, project, root, left);
            left += width + RootGap;
            depth = Math.Max(depth, levels);
        }

        var totalWidth = left - RootGap;
        var totalHeight = (depth * (BoxHeight + LevelGap)) - LevelGap;
        var scale = Math.Min(1.0, Math.Min(area.Width / totalWidth, area.Height / totalHeight));
        if (scale < 0.45)
        {
            warnings.Add("Tek hat şeması çizim alanına çok küçük sığdı; büyük bir şablon seçin ya da besleme sayısını azaltın.");
        }

        var offsetX = area.X + ((area.Width - (totalWidth * scale)) / 2);
        var offsetY = area.Y + 4;
        items.AddRange(drawn.Select(item => ScenePlacement.Place(item, scale, offsetX, offsetY)));
    }

    // Ağacı soldan sağa yaprak sırasıyla yerleştirir; kök başına (genişlik, seviye sayısı) döner.
    private static (double Width, int Levels) Draw(List<SceneItem> items, Project project, PowerNode root, double left)
    {
        var leaves = Leaves(root);
        var width = (leaves * (BoxWidth + ColumnGap)) - ColumnGap;
        var levels = 0;

        double Place(PowerNode node, int level, double slotStart, out double slotWidth)
        {
            levels = Math.Max(levels, level + 1);
            var childCenters = new List<double>();
            var cursor = slotStart;
            foreach (var child in node.Children)
            {
                var centre = Place(child, level + 1, cursor, out var childWidth);
                childCenters.Add(centre);
                cursor += childWidth + ColumnGap;
            }

            slotWidth = node.Children.Count == 0 ? BoxWidth : cursor - ColumnGap - slotStart;
            var cx = node.Children.Count == 0 ? slotStart + (BoxWidth / 2) : (childCenters.Min() + childCenters.Max()) / 2;
            var top = level * (BoxHeight + LevelGap);
            DrawBox(items, project, node, cx - (BoxWidth / 2), top);

            if (node.Children.Count > 0)
            {
                var bottom = top + BoxHeight;
                var bus = bottom + (LevelGap / 2);
                items.Add(new LineItem(cx, bottom, cx, bus, 0.5));
                items.Add(new LineItem(childCenters.Min(), bus, childCenters.Max(), bus, 0.5));
                for (var i = 0; i < node.Children.Count; i++)
                {
                    var childTop = (level + 1) * (BoxHeight + LevelGap);
                    items.Add(new LineItem(childCenters[i], bus, childCenters[i], childTop, 0.5));
                    DrawEdgeLabel(items, node.Children[i], childCenters[i], bus, childTop);
                }
            }

            return cx;
        }

        Place(root, 0, left, out _);
        return (width, levels);
    }

    private static int Leaves(PowerNode node) => node.Children.Count == 0 ? 1 : node.Children.Sum(Leaves);

    private static void DrawBox(List<SceneItem> items, Project project, PowerNode node, double x, double y)
    {
        items.Add(new RectItem(x, y, BoxWidth, BoxHeight, node.Kind == PowerElementKind.Source ? 0.6 : 0.4));

        // Sembol: kutunun solunda, kutuya sığacak ölçüde.
        var symbol = SymbolLibrary.For(node.Function, project);
        var symbolHeight = Math.Max(symbol.Height - symbol.MinY, 1);
        var scale = Math.Min(1.0, (BoxHeight - 6) / symbolHeight);
        var symbolWidth = Math.Max(symbol.MaxX - symbol.MinX, 1) * scale;
        var (sx, sy) = (x + 4 - (symbol.MinX * scale), y + 3 - (symbol.MinY * scale));
        if (node.Kind != PowerElementKind.Source)
        {
            items.AddRange(symbol.Items.Select(item => ScenePlacement.Place(item, scale, sx, sy)));
        }

        var centred = node.Kind == PowerElementKind.Source;
        var textX = centred ? x + (BoxWidth / 2) : x + 8 + symbolWidth;
        var align = centred ? TextAlign.Center : TextAlign.Left;
        var available = centred ? BoxWidth - 4 : BoxWidth - (textX - x) - 1;
        var maxChars = (int)Math.Max(6, available / 1.1);
        items.Add(new TextItem(textX, y + 7, $"-{node.Device.ProductTag}", 3.2, FontFamily, align));

        // Açıklama en çok iki satır; sığmayan sondan kısalır.
        var lines = Wrap(node.Device.Description, maxChars);
        for (var i = 0; i < lines.Count; i++)
        {
            items.Add(new TextItem(textX, y + 12 + (i * 3.2), lines[i], 2.1, FontFamily, align));
        }

        var detail = centred
            ? string.Join("  ", node.Potentials)
            : node.RatedCurrentA is { } rating ? $"{Number(rating)} A" : KindLabel(node.Kind);
        items.Add(new TextItem(textX, y + 22, Trim(detail, maxChars), 2.5, FontFamily, align));
    }

    // Açıklamayı sözcük sınırından en çok iki satıra böler.
    private static List<string> Wrap(string? text, int max)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return lines;
        }

        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > max)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = current.Length == 0 ? word : $"{current} {word}";
            }
        }

        lines.Add(current);
        return [.. lines.Take(2).Select((line, index) => index == 1 && lines.Count > 2 ? Trim(line + "…", max) : Trim(line, max))];
    }

    private static string Number(decimal value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    // Kenar etiketi: telin kesiti ve (kablo damarıysa) kablonun adı ve tipi; çocuğa inen dikey çizginin yanında.
    private static void DrawEdgeLabel(List<SceneItem> items, PowerNode child, double x, double fromY, double toY)
    {
        var lines = new List<string>();
        if (child.CrossSectionMm2 is { } section)
        {
            lines.Add($"{Number(section)} mm²");
        }

        if (child.Cable is { } cable)
        {
            lines.Add(cable);
        }

        var y = fromY + ((toY - fromY) / 2) + 1 - ((lines.Count - 1) * 1.6);
        for (var i = 0; i < lines.Count; i++)
        {
            items.Add(new TextItem(x + 1.2, y + (i * 3.2), lines[i], 2.1, FontFamily));
        }
    }

    private static string KindLabel(PowerElementKind kind) => kind switch
    {
        PowerElementKind.Switch => "Anahtarlama",
        PowerElementKind.Protection => "Koruma",
        PowerElementKind.Load => "Yük",
        _ => string.Empty,
    };

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

/// <summary>Mantıksal koordinattaki bir sahne öğesini ölçekleyip sayfa koordinatına kaydırır; kalınlık ve yazı boyutu da ölçeklenir.</summary>
internal static class ScenePlacement
{
    public static SceneItem Place(SceneItem item, double scale, double offsetX, double offsetY) => item switch
    {
        LineItem line => line with
        {
            X1 = (line.X1 * scale) + offsetX,
            Y1 = (line.Y1 * scale) + offsetY,
            X2 = (line.X2 * scale) + offsetX,
            Y2 = (line.Y2 * scale) + offsetY,
            StrokeMm = line.StrokeMm * Math.Max(scale, 0.6),
        },
        RectItem rect => rect with
        {
            X = (rect.X * scale) + offsetX,
            Y = (rect.Y * scale) + offsetY,
            Width = rect.Width * scale,
            Height = rect.Height * scale,
            StrokeMm = rect.StrokeMm * Math.Max(scale, 0.6),
        },
        CircleItem circle => circle with
        {
            X = (circle.X * scale) + offsetX,
            Y = (circle.Y * scale) + offsetY,
            Radius = circle.Radius * scale,
            StrokeMm = circle.StrokeMm * Math.Max(scale, 0.6),
        },
        TextItem text => text with { X = (text.X * scale) + offsetX, Y = (text.Y * scale) + offsetY, SizeMm = text.SizeMm * scale },
        _ => item,
    };
}
