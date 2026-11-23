using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Editor;

/// <summary>
/// Klemens şeridi penceresindeki bir klemens. <c>Id</c> boşsa yeni klemenstir. <c>JumperInner</c> ve <c>JumperOuter</c>, bu
/// klemensin iç (a) ve dış (b) noktasının listedeki <b>bir sonraki</b> klemensin aynı noktasına telsiz köprülü olduğunu söyler.
/// <c>Connections</c> kendi şeridi dışına giden bağlantı sayısıdır (tel, kablo damarı); sıfırdan büyükse klemens kaldırılamaz.
/// </summary>
public sealed record TerminalRow(
    FunctionId? Id,
    string Name,
    bool JumperInner,
    bool JumperOuter,
    int Connections,
    string Where,
    int Level = 1,
    int Ways = 1,
    TerminalRole Role = TerminalRole.Normal);

public sealed record TerminalStripData(DeviceId Device, string Designation, IReadOnlyList<TerminalRow> Terminals);

/// <summary>Pencerenin döndürdüğü nihai liste: sıra, adlar, köprüler; kaldırılanlar listede yoktur.</summary>
public sealed record TerminalStripEdits(DeviceId Device, IReadOnlyList<TerminalRow> Terminals);

/// <summary>Klemens şeridi düzenleyicisi, oturum tarafı (Faz 6.3; ADR-0030).</summary>
public sealed partial class EditorSession
{
    /// <summary>Yerleşimin cihazı bir klemens şeridiyse kimliği; değilse <c>null</c>.</summary>
    public DeviceId? StripOfPlacement(PlacementId placementId)
    {
        if (Project is not { } project
            || project.Placements.FirstOrDefault(placement => placement.Id == placementId) is not { } found)
        {
            return null;
        }

        return project.Devices.FirstOrDefault(device => device.Kind == DeviceKind.TerminalStrip && device.Functions.Any(function => function.Id == found.FunctionId))?.Id;
    }

    /// <summary>Şeridin klemensleri sırayla; şerit değilse ya da yoksa <c>null</c>.</summary>
    public TerminalStripData? TerminalStripOf(DeviceId stripId)
    {
        if (Project?.FindDevice(stripId) is not { Kind: DeviceKind.TerminalStrip } strip)
        {
            return null;
        }

        var project = Project;
        var stripPoints = strip.Functions.SelectMany(function => function.ConnectionPoints).Select(point => point.Id).ToHashSet();
        var rows = new List<TerminalRow>();
        for (var i = 0; i < strip.Functions.Count; i++)
        {
            var terminal = strip.Functions[i];
            var points = terminal.ConnectionPoints.Select(point => point.Id).ToHashSet();
            var external = project.Connections.Count(connection =>
                (points.Contains(connection.From) || points.Contains(connection.To))
                && !(stripPoints.Contains(connection.From) && stripPoints.Contains(connection.To)));
            var next = i + 1 < strip.Functions.Count ? strip.Functions[i + 1] : null;
            rows.Add(new TerminalRow(
                terminal.Id,
                terminal.Description ?? terminal.ConnectionPoints.FirstOrDefault()?.Name ?? string.Empty,
                next is not null && FindJumper(terminal, next, 0) is not null,
                next is not null && FindJumper(terminal, next, 1) is not null,
                external,
                Locate(terminal.Id.Value) is { } at ? $"sayfa {project.Pages[at.PageIndex].Number}" : "yerleştirilmemiş",
                terminal.Level,
                terminal.Ways == 0 ? 1 : terminal.Ways,
                terminal.Role));
        }

        return new TerminalStripData(strip.Id, project.DesignationOf(strip).ToString(), rows);
    }

    /// <summary>Yeni klemensin önerilen adı: adlar sayıysa en büyüğünün bir fazlası, değilse sıra numarası; hep tekil.</summary>
    public static string NextTerminalName(IEnumerable<string> names)
    {
        var existing = names.ToHashSet();
        var numbers = existing.Select(name => int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null).OfType<int>().ToList();
        var next = numbers.Count > 0 ? numbers.Max() + 1 : existing.Count + 1;
        while (existing.Contains(next.ToString(CultureInfo.InvariantCulture)))
        {
            next++;
        }

        return next.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Pencerenin nihai listesini tek adımda uygular: kaldırılanlar, yeniden adlandırmalar (klemens adı ve a/b noktaları), yeni
    /// klemensler, sıra ve bitişik klemensler arasındaki köprüler. Biri reddedilirse hiçbiri uygulanmaz.
    /// </summary>
    /// <remarks>
    /// Köprü bayrağı yalnızca bitişik klemensleri yönetir. Bayrak işaretliyse ve köprü yoksa kurulur; işaretsizse ancak o
    /// çift daha önce de bitişikti ve köprülüydü ise kaldırılır. Bitişik olmayan klemensler arasındaki köprülere dokunulmaz.
    /// </remarks>
    public Result ApplyTerminalStrip(TerminalStripEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (Project is null)
        {
            return NoProject();
        }

        if (Project.FindDevice(edits.Device) is not { Kind: DeviceKind.TerminalStrip } strip)
        {
            return Result.Failure(new Error("device.serit_degil", "Klemens şeridi bulunamadı."));
        }

        var rows = edits.Terminals.Select(row => row with { Name = row.Name.Trim() }).ToList();
        if (rows.Count == 0)
        {
            return Result.Failure(new Error("terminal.son", $"-{strip.ProductTag} şeridinde en az bir klemens kalmalı."));
        }

        if (rows.FirstOrDefault(row => row.Name.Length == 0 || row.Name.Any(char.IsWhiteSpace) || row.Name.Contains(':', StringComparison.Ordinal)) is { } bad)
        {
            return Result.Failure(new Error("terminal.ad_gecersiz", $"Klemens adı boş olamaz, boşluk ya da ':' içeremez: '{bad.Name}'"));
        }

        if (rows.FirstOrDefault(row => row.Ways is < 1 or > Function.MaxWays) is { } badWays)
        {
            return Result.Failure(new Error(
                "terminal.baglanti_gecersiz", $"Taraf başına bağlantı sayısı 1 ile {Function.MaxWays} arasında olmalı: {badWays.Ways}"));
        }

        if (rows.FirstOrDefault(row => row.Level is < 1 or > Function.MaxLevel) is { } badLevel)
        {
            return Result.Failure(new Error("terminal.kat_gecersiz", $"Klemens katı 1 ile {Function.MaxLevel} arasında olmalı: {badLevel.Level}"));
        }

        if (rows.GroupBy(row => (row.Name, row.Level)).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            return Result.Failure(new Error(
                "terminal.ad_tekrar",
                $"'{duplicate.Key.Name}' adlı birden çok klemens var" + (duplicate.Key.Level > 1 ? $" ({duplicate.Key.Level}. kat)." : ".")));
        }

        var current = strip.Functions.ToDictionary(function => function.Id);
        var kept = rows.Where(row => row.Id is { } id && current.ContainsKey(id)).Select(row => row.Id!.Value).ToHashSet();
        var commands = new List<ICommand>();

        // 1) Kaldırılanlar (köprüleri ve yerleşimleri birlikte gider).
        foreach (var removed in strip.Functions.Where(function => !kept.Contains(function.Id)))
        {
            commands.Add(new RemoveTerminal(strip.Id, removed.Id));
        }

        // 2) Yeniden adlandırma: önce, çünkü yeni klemensin adı eski bir klemensin adı olabilir.
        foreach (var row in rows.Where(row => row.Id is { } id && current.ContainsKey(id)))
        {
            var terminal = current[row.Id!.Value];
            if (terminal.Description != row.Name)
            {
                commands.Add(new SetFunctionDescription(terminal.Id, row.Name));
            }

            if (terminal.Level != row.Level)
            {
                commands.Add(new SetTerminalLevel(strip.Id, terminal.Id, row.Level));
            }

            // Bağlantı sayısı ve sonlandırıcı olma, eklerken verilir; sonradan değişmez (noktalar eklenip silinirdi).
            if (terminal.Ways > 0 && row.Role != TerminalRole.EndCover && row.Ways != terminal.Ways)
            {
                return Result.Failure(new Error(
                    "terminal.baglanti_degismez", $"'{terminal.Description}' klemensinin bağlantı sayısı değiştirilemez; kaldırıp yenisini ekleyin."));
            }

            if (row.Role != terminal.Role)
            {
                commands.Add(new SetTerminalRole(strip.Id, terminal.Id, row.Role));
            }

            var names = TerminalNames.Points(row.Name, row.Level, terminal.Ways);
            for (var i = 0; i < Math.Min(names.Count, terminal.ConnectionPoints.Count); i++)
            {
                if (terminal.ConnectionPoints[i].Name != names[i])
                {
                    commands.Add(new RenameConnectionPoint(terminal.ConnectionPoints[i].Id, names[i]));
                }
            }
        }

        // 3) Yeni klemensler sona eklenir, 4) sonra istenen sıraya taşınır.
        var target = new List<Function>();
        var order = strip.Functions.Where(function => kept.Contains(function.Id)).ToList();
        foreach (var row in rows)
        {
            if (row.Id is { } id && current.TryGetValue(id, out var existing))
            {
                target.Add(existing);
                continue;
            }

            var created = AddTerminal.Create(row.Name, row.Level, row.Ways, row.Role);
            commands.Add(new AddTerminal(strip.Id, order.Count, created));
            order.Add(created);
            target.Add(created);
        }

        for (var i = 0; i < target.Count; i++)
        {
            if (order[i] != target[i])
            {
                commands.Add(new MoveTerminal(strip.Id, target[i].Id, i));
                order.Remove(target[i]);
                order.Insert(i, target[i]);
            }
        }

        // 5) Köprüler: bitişik çiftler arasında bayrağa göre.
        var originalNext = new Dictionary<FunctionId, Function>();
        for (var i = 0; i + 1 < strip.Functions.Count; i++)
        {
            originalNext[strip.Functions[i].Id] = strip.Functions[i + 1];
        }

        for (var i = 0; i + 1 < target.Count; i++)
        {
            var (a, b) = (target[i], target[i + 1]);
            foreach (var side in new[] { 0, 1 })
            {
                var wanted = side == 0 ? rows[i].JumperInner : rows[i].JumperOuter;
                var existing = current.ContainsKey(a.Id) && current.ContainsKey(b.Id) ? FindJumper(a, b, side) : null;
                var (pointA, pointB) = (TerminalNames.OnSide(a, side).FirstOrDefault(), TerminalNames.OnSide(b, side).FirstOrDefault());
                if (wanted && existing is null && pointA is not null && pointB is not null)
                {
                    commands.Add(new Connect(new Connection(ConnectionId.New(), pointA.Id, pointB.Id)));
                }
                else if (!wanted && existing is not null && originalNext.TryGetValue(a.Id, out var was) && was == b)
                {
                    commands.Add(new Disconnect(existing.Id));
                }
            }
        }

        return ExecuteIfAny(new Transaction($"Klemens şeridi düzenlendi: -{strip.ProductTag}", commands));
    }

    // İki klemensin aynı tarafındaki (0 iç, 1 dış) noktaları arasındaki telsiz bağlantı (köprü); yoksa null.
    private Connection? FindJumper(Function a, Function b, int side)
    {
        if (TerminalNames.OnSide(a, side).FirstOrDefault() is not { } first || TerminalNames.OnSide(b, side).FirstOrDefault() is not { } second)
        {
            return null;
        }

        var (pa, pb) = (first.Id, second.Id);
        return Project!.Connections.FirstOrDefault(connection =>
            connection.Wire is null && connection.CoreId is null
            && ((connection.From == pa && connection.To == pb) || (connection.From == pb && connection.To == pa)));
    }
}
