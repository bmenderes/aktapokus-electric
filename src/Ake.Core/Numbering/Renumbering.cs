using System.Globalization;
using System.Text.RegularExpressions;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Numbering;

public enum WireNumberingScheme
{
    /// <summary>Her potansiyele bir numara; aynı potansiyeldeki teller aynı numarayı taşır.</summary>
    PerPotential,

    /// <summary>Her tele ayrı numara.</summary>
    Sequential,
}

/// <summary>
/// Otomatik numaralandırma. Modeli doğrudan değiştirmez; çalıştırılacak bir <see cref="Transaction"/>
/// üretir. Böylece yeniden numaralandırma tek adımda geri alınır ve audit log'a yazılır.
/// </summary>
public static partial class Renumbering
{
    [GeneratedRegex(@"^(?<prefix>\D+)\d+$")]
    private static partial Regex LetterNumberTag();

    [GeneratedRegex(@"^\d+(?<rest>\D+\d+)$")]
    private static partial Regex LeadingDigits();

    /// <summary>
    /// Device tag'lerini projenin tag şemasına göre yeniden numaralar (<see cref="Project.TagScheme"/>).
    /// </summary>
    public static Transaction Tags(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new Transaction("Device tag'leri yeniden numaralandı", Retags(project, project.TagScheme));
    }

    /// <summary>
    /// Projenin tag şemasını değiştirir ve bütün tag'leri yeni şemaya göre yeniden numaralar; ikisi tek
    /// adımdır, birlikte geri alınır.
    /// </summary>
    public static Transaction ChangeTagScheme(Project project, TagScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(project);

        List<ICommand> commands = project.TagScheme == scheme ? [] : [new SetTagScheme(scheme)];
        commands.AddRange(Retags(project, scheme));
        return new Transaction($"Tag şeması değişti: {scheme}", commands);
    }

    /// <summary>
    /// <see cref="TagScheme.LetterNumber"/>: aynı yapı düğümleri ve aynı harf içinde model sırasıyla
    /// 1'den başlayarak boşluksuz. <see cref="TagScheme.PageLetterNumber"/>: cihazın ilk göründüğü
    /// sayfanın numarası + harf + o sayfada aynı harfi taşıyanlar arasındaki sıra; sıra sembolün
    /// yerine göre soldan sağa, sonra yukarıdan aşağıdır. Harfi çıkarılamayan tag'lere ve sayfa
    /// şemasında hiçbir sayfada olmayan cihazlara dokunulmaz.
    /// </summary>
    private static List<ICommand> Retags(Project project, TagScheme scheme)
    {
        var pageNumbers = project.Pages.Select(page => page.Number).ToList();
        var candidates = new List<(Device Device, string Prefix)>();
        foreach (var device in project.Devices)
        {
            if (PrefixOf(device.ProductTag, pageNumbers) is { } prefix)
            {
                candidates.Add((device, prefix));
            }
        }

        var tags = scheme == TagScheme.PageLetterNumber
            ? PageTags(project, candidates)
            : LetterTags(candidates);

        var commands = new List<ICommand>();
        foreach (var (device, tag) in tags)
        {
            if (tag != device.ProductTag)
            {
                commands.Add(new Retag(device.Id, tag, device.FunctionNodeId, device.LocationNodeId));
            }
        }

        return commands;
    }

    private static IEnumerable<(Device Device, string Tag)> LetterTags(List<(Device Device, string Prefix)> candidates)
    {
        var counters = new Dictionary<(StructureNodeId?, StructureNodeId?, string), int>();
        foreach (var (device, prefix) in candidates)
        {
            var key = (device.FunctionNodeId, device.LocationNodeId, prefix);
            var number = counters.GetValueOrDefault(key) + 1;
            counters[key] = number;
            yield return (device, prefix + number.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static IEnumerable<(Device Device, string Tag)> PageTags(Project project, List<(Device Device, string Prefix)> candidates)
    {
        var pageIndex = project.Pages.Select((page, index) => (page.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index);

        // Cihazın evi: ana görünümlerinden sayfa sırası en küçük olanı; o sayfada en soldaki, en üstteki.
        var homes = new List<(Device Device, string Prefix, int Page, double X, double Y, int Order)>();
        for (var order = 0; order < candidates.Count; order++)
        {
            var (device, prefix) = candidates[order];
            var functions = device.Functions.Select(function => function.Id).ToHashSet();
            var home = project.Placements
                .Where(placement => placement.Role == PlacementRole.Main
                    && functions.Contains(placement.FunctionId)
                    && pageIndex.ContainsKey(placement.PageId))
                .OrderBy(placement => pageIndex[placement.PageId])
                .ThenBy(placement => placement.X)
                .ThenBy(placement => placement.Y)
                .FirstOrDefault();
            if (home is not null)
            {
                homes.Add((device, prefix, pageIndex[home.PageId], home.X, home.Y, order));
            }
        }

        foreach (var group in homes.GroupBy(home => (home.Page, home.Prefix)))
        {
            var number = 0;
            var pageNumber = project.Pages[group.Key.Page].Number;
            foreach (var home in group.OrderBy(home => home.X).ThenBy(home => home.Y).ThenBy(home => home.Order))
            {
                number++;
                yield return (home.Device, pageNumber + home.Prefix + number.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>
    /// Tag'in harf öneki: <c>K1</c> → <c>K</c>, <c>3K1</c> → <c>K</c>. Önce projedeki sayfa numaralarından
    /// en uzun eşleşeni baştan atılır (<c>2a</c> sayfasındaki <c>2aK1</c>), sonra baştaki rakamlar.
    /// </summary>
    internal static string? PrefixOf(string tag, List<string> pageNumbers)
    {
        foreach (var page in pageNumbers.Where(number => number.Length > 0).OrderByDescending(number => number.Length))
        {
            if (tag.Length > page.Length
                && tag.StartsWith(page, StringComparison.Ordinal)
                && LetterNumberTag().Match(tag[page.Length..]) is { Success: true } paged)
            {
                return paged.Groups["prefix"].Value;
            }
        }

        var rest = LeadingDigits().Match(tag) is { Success: true } leading ? leading.Groups["rest"].Value : tag;
        return LetterNumberTag().Match(rest) is { Success: true } match ? match.Groups["prefix"].Value : null;
    }

    /// <summary>
    /// <b>Baştan numaralar</b>: teli olan bütün bağlantıları model sırasına göre, projenin tel numaralandırma ayarlarına (kod, başlangıç, adım,
    /// basamak) göre yeniden numaralar. Şema parametre ile ezilebilir. Kablo damarlarına dokunmaz. Kullanıcı isterse yapılır; mevcut numaralar değişir.
    /// </summary>
    public static Transaction Wires(Project project, WireNumberingScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Wires(project, project.WireNumbering with { Scheme = scheme });
    }

    /// <summary>Baştan numaralar; ayarlar verilir (projenin ayarlarını değiştirmez).</summary>
    public static Transaction Wires(Project project, WireNumberSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);

        var connectivity = Connectivity.Compute(project);
        var numberByPotential = new Dictionary<Potential, int>(ReferenceEqualityComparer.Instance);
        var commands = new List<ICommand>();
        var next = settings.Start - settings.Step;

        foreach (var connection in project.Connections)
        {
            if (connection.Wire is not { } wire)
            {
                continue;
            }

            int number;
            if (settings.Scheme == WireNumberingScheme.Sequential)
            {
                number = next += settings.Step;
            }
            else
            {
                var potential = connectivity.PotentialOf(connection.From);
                if (!numberByPotential.TryGetValue(potential, out number))
                {
                    number = next += settings.Step;
                    numberByPotential[potential] = number;
                }
            }

            var text = settings.Format(number);
            if (text != wire.Number)
            {
                commands.Add(new AssignWire(
                    connection.Id, new Wire(wire.Id, text, wire.CrossSectionMm2, wire.Color), coreId: null));
            }
        }

        return new Transaction("Tel numaraları baştan verildi", commands);
    }

    /// <summary>
    /// <b>Yeni telleri numaralar</b> (kaldığı yerden): yalnızca numarası olmayan telleri numaralar; mevcut numaralara dokunmaz. Sıradaki numara,
    /// projenin kodunu taşıyan en büyük numaranın bir adım ötesidir (hiç yoksa başlangıç). Potansiyel bazlı şemada, numarası olan bir potansiyele
    /// bağlı yeni tel o potansiyelin numarasını alır. Araya sayfa eklenince eski numaralar böylece korunur.
    /// </summary>
    public static Transaction NewWires(Project project) => NewWires(project, (project ?? throw new ArgumentNullException(nameof(project))).WireNumbering);

    /// <summary>Yeni telleri numaralar; ayarlar verilir (projenin ayarlarını değiştirmez).</summary>
    public static Transaction NewWires(Project project, WireNumberSettings settings)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(settings);

        var connectivity = Connectivity.Compute(project);

        // Mevcut numaralar: potansiyel başına bilinen numara ve kodu taşıyan en büyük sayı.
        var numberByPotential = new Dictionary<Potential, string>(ReferenceEqualityComparer.Instance);
        var highest = settings.Start - settings.Step;
        foreach (var connection in project.Connections.Where(connection => !string.IsNullOrEmpty(connection.Wire?.Number)))
        {
            numberByPotential.TryAdd(connectivity.PotentialOf(connection.From), connection.Wire!.Number!);
            if (settings.TryRead(connection.Wire.Number, out var value) && value > highest)
            {
                highest = value;
            }
        }

        var commands = new List<ICommand>();
        var next = highest;
        foreach (var connection in project.Connections)
        {
            if (connection.Wire is not { } wire || !string.IsNullOrEmpty(wire.Number))
            {
                continue;
            }

            string text;
            var potential = connectivity.PotentialOf(connection.From);
            if (settings.Scheme == WireNumberingScheme.PerPotential && numberByPotential.TryGetValue(potential, out var existing))
            {
                text = existing;
            }
            else
            {
                text = settings.Format(next += settings.Step);
                if (settings.Scheme == WireNumberingScheme.PerPotential)
                {
                    numberByPotential[potential] = text;
                }
            }

            commands.Add(new AssignWire(connection.Id, new Wire(wire.Id, text, wire.CrossSectionMm2, wire.Color), coreId: null));
        }

        return new Transaction("Yeni teller numaralandı", commands);
    }
}
