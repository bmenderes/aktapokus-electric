using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Klemens şeridine klemens ekler (ADR-0030). Klemens, <c>Terminal</c> türünde ve <c>PassThrough</c> bir function'dır; adı
/// <c>Description</c>, iç ve dış noktaları <c>ad.a</c> ve <c>ad.b</c>'dir. Ad boş olamaz, boşluk ya da ':' içeremez ve şeritte
/// tekil olmalıdır.
/// </summary>
public sealed class AddTerminal(DeviceId stripId, int index, Function terminal) : ICommand
{
    private Device? _strip;

    public string Description => $"Klemens eklendi: {terminal.Description}";

    /// <summary>
    /// Verilen adla yeni klemens: iç nokta <c>ad.a</c>, dış nokta <c>ad.b</c> (2. ve 3. katta <c>ad.a2</c>, <c>ad.b3</c>…). Çok bağlantılı
    /// klemenste ek noktalar <c>ad.a.2</c>, <c>ad.b.2</c>… (ADR-0040). Sonlandırıcı (<see cref="TerminalRole.EndCover"/>) nokta taşımaz.
    /// </summary>
    public static Function Create(string name, int level = 1, int ways = 1, TerminalRole role = TerminalRole.Normal) => new(
        FunctionId.New(),
        FunctionKind.Terminal,
        role == TerminalRole.EndCover
            ? []
            : TerminalNames.Points(name, level, ways).Select(point => new ConnectionPoint(ConnectionPointId.New(), point)),
        passThrough: role != TerminalRole.EndCover,
        description: name,
        level: level,
        role: role);

    public Result Execute(Project project)
    {
        if (Strip(project, stripId) is not { } strip)
        {
            return Failure(project, stripId);
        }

        var name = terminal.Description;
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace) || name.Contains(':', StringComparison.Ordinal))
        {
            return Result.Failure(new Error("terminal.ad_gecersiz", $"Klemens adı boş olamaz, boşluk ya da ':' içeremez: '{name}'"));
        }

        if (terminal.Level is < 1 or > Function.MaxLevel)
        {
            return Result.Failure(new Error("terminal.kat_gecersiz", $"Klemens katı 1 ile {Function.MaxLevel} arasında olmalı: {terminal.Level}"));
        }

        var points = terminal.ConnectionPoints.Count;
        if (terminal.Role == TerminalRole.EndCover
            ? points != 0
            : points is < 2 or > 2 * Function.MaxWays || points % 2 != 0)
        {
            return Result.Failure(new Error(
                "terminal.baglanti_gecersiz",
                $"Klemensin taraf başına bağlantı sayısı 1 ile {Function.MaxWays} arasında olmalı (sonlandırıcıda bağlantı noktası olmaz): {points} nokta"));
        }

        if (strip.Functions.Any(other => other.Description == name && other.Level == terminal.Level))
        {
            return Result.Failure(new Error(
                "terminal.ad_tekrar",
                $"-{strip.ProductTag} şeridinde '{name}' adlı klemens zaten var" + (terminal.Level > 1 ? $" ({terminal.Level}. kat)." : ".")));
        }

        if (index < 0 || index > strip.Functions.Count)
        {
            return Result.Failure(new Error("terminal.sira_gecersiz", $"Klemens sırası 0 ile {strip.Functions.Count} arasında olmalı: {index}"));
        }

        if (project.IdSet().Overlaps(new[] { terminal.Id.Value }.Concat(terminal.ConnectionPoints.Select(point => point.Id.Value))))
        {
            return Result.Failure(new Error("id.tekrar", "Klemensin kimliği zaten kullanılıyor."));
        }

        _strip = strip;
        project.InsertFunction(strip, index, terminal);
        return Result.Success();
    }

    public void Undo(Project project) => project.RemoveFunction(_strip!, terminal);

    internal static Device? Strip(Project project, DeviceId id) =>
        project.FindDevice(id) is { Kind: DeviceKind.TerminalStrip } strip ? strip : null;

    internal static Result Failure(Project project, DeviceId id) => project.FindDevice(id) is null
        ? Result.Failure(new Error("device.yok", $"Device modelde yok ({id})."))
        : Result.Failure(new Error("device.serit_degil", $"-{project.FindDevice(id)!.ProductTag} bir klemens şeridi değil."));
}

/// <summary>
/// Klemensi şeritten kaldırır: aynı şeritteki klemenslere giden köprüleri, yerleşimlerini ve potansiyel işaretlerini birlikte
/// götürür, PLC kanalı ataması varsa boşaltır. Tel ya da kablo damarı bağlı klemens kaldırılmaz (önce bağlantı kaldırılır);
/// şeritte en az bir klemens kalır.
/// </summary>
public sealed class RemoveTerminal(DeviceId stripId, FunctionId terminalId) : ICommand
{
    private Device? _strip;
    private Function? _terminal;
    private int _index;
    private List<(int Index, Connection Item)> _connections = [];
    private List<(int Index, PotentialSource Item)> _sources = [];
    private List<(int Index, Placement Item)> _placements = [];
    private List<(Channel Channel, FunctionId FunctionId)> _channels = [];

    public string Description => _terminal is null ? "Klemens kaldırıldı" : $"Klemens kaldırıldı: {_terminal.Description}";

    public Result Execute(Project project)
    {
        if (AddTerminal.Strip(project, stripId) is not { } strip)
        {
            return AddTerminal.Failure(project, stripId);
        }

        var terminal = strip.Functions.FirstOrDefault(function => function.Id == terminalId);
        if (terminal is null)
        {
            return Result.Failure(new Error("function.yok", $"Klemens şeritte yok ({terminalId})."));
        }

        if (strip.Functions.Count == 1)
        {
            return Result.Failure(new Error("terminal.son", $"-{strip.ProductTag} şeridinde en az bir klemens kalmalı."));
        }

        var stripPoints = strip.Functions.SelectMany(function => function.ConnectionPoints).Select(point => point.Id).ToHashSet();
        var points = terminal.ConnectionPoints.Select(point => point.Id).ToHashSet();
        var wired = project.Connections.FirstOrDefault(connection =>
            (points.Contains(connection.From) || points.Contains(connection.To))
            && !(stripPoints.Contains(connection.From) && stripPoints.Contains(connection.To)));
        if (wired is not null)
        {
            return Result.Failure(new Error(
                "terminal.bagli",
                $"-{strip.ProductTag}:{terminal.Description} klemensine tel ya da kablo damarı bağlı; önce bağlantıyı kaldırın."));
        }

        _strip = strip;
        _terminal = terminal;
        _connections = ListEdit.RemoveWhere(
            project.ConnectionList, connection => points.Contains(connection.From) || points.Contains(connection.To));
        _sources = ListEdit.RemoveWhere(project.PotentialSourceList, source => points.Contains(source.At));
        _placements = ListEdit.RemoveWhere(project.PlacementList, placement => placement.FunctionId == terminalId);
        _channels = [];
        foreach (var channel in project.Channels.Where(channel => channel.FunctionId == terminalId))
        {
            _channels.Add((channel, terminalId));
            channel.FunctionId = null;
        }

        _index = project.RemoveFunction(strip, terminal);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        project.InsertFunction(_strip!, _index, _terminal!);
        ListEdit.Restore(project.ConnectionList, _connections);
        ListEdit.Restore(project.PotentialSourceList, _sources);
        ListEdit.Restore(project.PlacementList, _placements);
        foreach (var (channel, functionId) in _channels)
        {
            channel.FunctionId = functionId;
        }
    }
}

/// <summary>Klemensi şerit içinde başka bir sıraya taşır (0 tabanlı, taşıdıktan sonraki sıra).</summary>
public sealed class MoveTerminal(DeviceId stripId, FunctionId terminalId, int newIndex) : ICommand
{
    private Device? _strip;
    private Function? _terminal;
    private int _previous;

    public string Description => _terminal is null ? "Klemens taşındı" : $"Klemens taşındı: {_terminal.Description} → {newIndex + 1}. sıra";

    public Result Execute(Project project)
    {
        if (AddTerminal.Strip(project, stripId) is not { } strip)
        {
            return AddTerminal.Failure(project, stripId);
        }

        var terminal = strip.Functions.FirstOrDefault(function => function.Id == terminalId);
        if (terminal is null)
        {
            return Result.Failure(new Error("function.yok", $"Klemens şeritte yok ({terminalId})."));
        }

        if (newIndex < 0 || newIndex >= strip.Functions.Count)
        {
            return Result.Failure(new Error("terminal.sira_gecersiz", $"Klemens sırası 0 ile {strip.Functions.Count - 1} arasında olmalı: {newIndex}"));
        }

        _strip = strip;
        _terminal = terminal;
        _previous = strip.FunctionList.IndexOf(terminal);
        strip.FunctionList.RemoveAt(_previous);
        strip.FunctionList.Insert(newIndex, terminal);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _strip!.FunctionList.Remove(_terminal!);
        _strip.FunctionList.Insert(_previous, _terminal!);
    }
}

/// <summary>
/// Klemens nokta adları (ADR-0034, ADR-0040): 1. katta <c>ad.a</c> / <c>ad.b</c>, 2. ve 3. katta <c>ad.a2</c> / <c>ad.b3</c>. Çok bağlantılı
/// klemenste ilk bağlantı bu adı taşır, ötekiler sonuna <c>.2</c>, <c>.3</c> alır (<c>ad.a.2</c>, <c>ad.b2.3</c>).
/// </summary>
public static class TerminalNames
{
    public static string Point(string name, int level, int side, int index = 0) =>
        $"{name}.{(side == 0 ? 'a' : 'b')}{(level > 1 ? level.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty)}"
        + (index == 0 ? string.Empty : $".{(index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    /// <summary>Klemensin bütün nokta adları, noktaların sırasıyla: önce iç taraf, sonra dış taraf.</summary>
    public static IReadOnlyList<string> Points(string name, int level, int ways) =>
    [
        .. Enumerable.Range(0, 2).SelectMany(side => Enumerable.Range(0, Math.Max(0, ways)).Select(index => Point(name, level, side, index))),
    ];

    /// <summary>Klemensin bir tarafındaki (0 iç, 1 dış) noktalar.</summary>
    public static IEnumerable<ConnectionPoint> OnSide(Function terminal, int side)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        var ways = terminal.Ways;
        return ways == 0 ? [] : terminal.ConnectionPoints.Skip(side * ways).Take(ways);
    }
}

/// <summary>
/// Klemensin katını değiştirir ve a/b noktalarının adlarını katına göre yeniler (ADR-0034). Aynı adlı klemens o katta zaten
/// varsa reddedilir. Bağlantılar kimlikle tutulduğu için değişmez.
/// </summary>
public sealed class SetTerminalLevel(DeviceId stripId, FunctionId terminalId, int level) : ICommand
{
    private Function? _terminal;
    private int _previousLevel;
    private List<string> _previousNames = [];

    public string Description => $"Klemens katı değişti: {level}. kat";

    public Result Execute(Project project)
    {
        if (AddTerminal.Strip(project, stripId) is not { } strip)
        {
            return AddTerminal.Failure(project, stripId);
        }

        var terminal = strip.Functions.FirstOrDefault(function => function.Id == terminalId);
        if (terminal is null)
        {
            return Result.Failure(new Error("function.yok", $"Klemens şeritte yok ({terminalId})."));
        }

        if (level is < 1 or > Function.MaxLevel)
        {
            return Result.Failure(new Error("terminal.kat_gecersiz", $"Klemens katı 1 ile {Function.MaxLevel} arasında olmalı: {level}"));
        }

        if (strip.Functions.Any(other => other != terminal && other.Description == terminal.Description && other.Level == level))
        {
            return Result.Failure(new Error(
                "terminal.ad_tekrar", $"-{strip.ProductTag} şeridinde '{terminal.Description}' adlı klemens {level}. katta zaten var."));
        }

        _terminal = terminal;
        _previousLevel = terminal.Level;
        _previousNames = [.. terminal.ConnectionPoints.Select(point => point.Name)];
        terminal.Level = level;
        var names = TerminalNames.Points(terminal.Description ?? string.Empty, level, terminal.Ways);
        for (var i = 0; i < Math.Min(names.Count, terminal.ConnectionPoints.Count); i++)
        {
            terminal.ConnectionPoints[i].Name = names[i];
        }

        return Result.Success();
    }

    public void Undo(Project project)
    {
        _terminal!.Level = _previousLevel;
        for (var i = 0; i < _previousNames.Count; i++)
        {
            _terminal.ConnectionPoints[i].Name = _previousNames[i];
        }
    }
}

/// <summary>
/// Klemensin türünü sıradan ile PE arasında değiştirir (ADR-0040). Sonlandırıcı ile diğerleri arasında geçiş yoktur (nokta sayısı değişirdi);
/// önce klemens kaldırılır, yenisi eklenir.
/// </summary>
public sealed class SetTerminalRole(DeviceId stripId, FunctionId terminalId, TerminalRole role) : ICommand
{
    private Function? _terminal;
    private TerminalRole _previous;

    public string Description => role == TerminalRole.ProtectiveEarth ? "Klemens PE olarak işaretlendi" : "Klemens sıradan yapıldı";

    public Result Execute(Project project)
    {
        if (AddTerminal.Strip(project, stripId) is not { } strip)
        {
            return AddTerminal.Failure(project, stripId);
        }

        var terminal = strip.Functions.FirstOrDefault(function => function.Id == terminalId);
        if (terminal is null)
        {
            return Result.Failure(new Error("function.yok", $"Klemens şeritte yok ({terminalId})."));
        }

        if (role == TerminalRole.EndCover || terminal.Role == TerminalRole.EndCover)
        {
            return Result.Failure(new Error(
                "terminal.tur_degismez", "Sonlandırıcıya ya da sonlandırıcıdan geçilemez; klemensi kaldırıp yenisini ekleyin."));
        }

        _terminal = terminal;
        _previous = terminal.Role;
        terminal.Role = role;
        return Result.Success();
    }

    public void Undo(Project project) => _terminal!.Role = _previous;
}
