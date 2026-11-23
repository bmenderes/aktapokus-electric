using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;

namespace Ake.Editor;

/// <summary>Navigator ya da mesaj panelinde bir satır; çift tıklanınca gidilecek sayfa ve sembol.</summary>
public sealed record NavigatorRow(string Label, int? PageIndex, PlacementId? Placement, DeviceId? Device = null)
{
    public override string ToString() => Label;
}

/// <summary>Özellik penceresinin gösterdikleri.</summary>
public sealed record DeviceProperties(
    DeviceId Device,
    string Designation,
    string ProductTag,
    string? Description,
    string? Part,
    string? TypeDesignation,
    IReadOnlyList<string> Functions);

/// <summary>Navigator'lar, mesaj paneli ve özellik penceresi için oturum verileri.</summary>
public sealed partial class EditorSession
{
    /// <summary>Cihaz navigator'ı: tam tag sırasıyla; nerede çizildiği ya da yerleştirilmediği yazılır.</summary>
    public IReadOnlyList<NavigatorRow> DeviceRows()
    {
        if (Project is not { } project)
        {
            return [];
        }

        return
        [
            .. project.Devices
                .Where(device => device.Kind != DeviceKind.Junction)
                .Select(device => (Device: device, Tag: project.DesignationOf(device).ToString()))
                .OrderBy(item => item.Tag, StringComparer.Ordinal)
                .Select(item =>
                {
                    var target = Locate(item.Device.Id.Value);
                    var where = target is { } found
                        ? $"sayfa {project.Pages[found.PageIndex].Number}"
                        : "yerleştirilmemiş";
                    var description = string.IsNullOrEmpty(item.Device.Description) ? string.Empty : $"  {item.Device.Description}";
                    return new NavigatorRow($"{item.Tag}{description}  ·  {where}", target?.PageIndex, target?.Placement, item.Device.Id);
                }),
        ];
    }

    /// <summary>Potansiyel navigator'ı: adı olan potansiyeller ve kaç bağlantı noktası taşıdıkları.</summary>
    public IReadOnlyList<NavigatorRow> PotentialRows()
    {
        if (Project is not { } project)
        {
            return [];
        }

        return
        [
            .. Connectivity.Compute(project).Potentials
                .Where(potential => potential.SourceNames.Count > 0)
                .Select(potential =>
                {
                    var points = potential.Points.ToHashSet();
                    var marker = project.PotentialSources.First(source => points.Contains(source.At));
                    var target = Locate(marker.At.Value);
                    var name = string.Join(" + ", potential.SourceNames);
                    return new NavigatorRow($"{name}  ·  {points.Count} nokta", target?.PageIndex, target?.Placement);
                })
                .OrderBy(row => row.Label, StringComparer.Ordinal),
        ];
    }

    /// <summary>Mesaj paneli: ERC bulguları, önce hatalar. Bulgunun nesnesi bir sayfadaysa oraya gidilir.</summary>
    public IReadOnlyList<NavigatorRow> FindingRows()
    {
        if (Erc is null)
        {
            return [];
        }

        return
        [
            .. Erc.Findings
                .OrderBy(finding => finding.Severity)
                .Select(finding =>
                {
                    var target = finding.Targets.Select(Locate).FirstOrDefault(found => found is not null);
                    var label = finding.Severity switch
                    {
                        Severity.Error => "Hata",
                        Severity.Warning => "Uyarı",
                        _ => "Bilgi",
                    };
                    return new NavigatorRow($"{label}  ·  {finding.Message}", target?.PageIndex, target?.Placement);
                }),
        ];
    }

    /// <summary>
    /// Bir nesnenin (device, function, bağlantı noktası, bağlantı, PLC kanalı, potansiyel işareti)
    /// çizildiği yer: ana görünümün sayfası ve yerleşimi. Hiçbir sayfada değilse <c>null</c>.
    /// </summary>
    public (int PageIndex, PlacementId Placement)? Locate(Guid target)
    {
        if (Project is null)
        {
            return null;
        }

        var index = LocateIndex();
        if (!index.Functions.TryGetValue(target, out var functions))
        {
            return null;
        }

        foreach (var functionId in functions)
        {
            if (index.Placed.TryGetValue(functionId, out var found))
            {
                return found;
            }
        }

        return null;
    }

    // Nesne kimliği → function'ları, function → ana görünümü (sayfa sırası, yerleşim). Navigator her satır için Locate
    // çağırır; her çağrıda modeli taramak yüzlerce sayfada O(n²) olurdu. İndeks model değişene kadar saklanır.
    private (int Version, Dictionary<Guid, List<FunctionId>> Functions, Dictionary<FunctionId, (int PageIndex, PlacementId Placement)> Placed)? _locateIndex;

    private (Dictionary<Guid, List<FunctionId>> Functions, Dictionary<FunctionId, (int PageIndex, PlacementId Placement)> Placed) LocateIndex()
    {
        if (_locateIndex is { } cached && cached.Version == _version)
        {
            return (cached.Functions, cached.Placed);
        }

        var project = Project!;
        var functions = new Dictionary<Guid, List<FunctionId>>();
        foreach (var device in project.Devices)
        {
            functions[device.Id.Value] = [.. device.Functions.Select(function => function.Id)];
            foreach (var function in device.Functions)
            {
                functions[function.Id.Value] = [function.Id];
                foreach (var point in function.ConnectionPoints)
                {
                    functions[point.Id.Value] = [function.Id];
                }
            }
        }

        foreach (var wire in project.Connections)
        {
            if (project.Locate(wire.From) is { } end)
            {
                functions[wire.Id.Value] = [end.Function.Id];
            }
        }

        foreach (var channel in project.Channels)
        {
            if (channel.FunctionId is { } function)
            {
                functions[channel.Id.Value] = [function];
            }
        }

        foreach (var marker in project.PotentialSources)
        {
            if (project.Locate(marker.At) is { } end)
            {
                functions[marker.Id.Value] = [end.Function.Id];
            }
        }

        // Function'ın ana görünümü; yoksa ilk başka görünümü.
        var pageIndexes = new Dictionary<PageId, int>();
        for (var i = 0; i < project.Pages.Count; i++)
        {
            pageIndexes[project.Pages[i].Id] = i;
        }

        var best = new Dictionary<FunctionId, Placement>();
        foreach (var placement in project.Placements)
        {
            if (!best.TryGetValue(placement.FunctionId, out var current) || (current.Role != PlacementRole.Main && placement.Role == PlacementRole.Main))
            {
                best[placement.FunctionId] = placement;
            }
        }

        var placed = new Dictionary<FunctionId, (int PageIndex, PlacementId Placement)>();
        foreach (var (function, placement) in best)
        {
            if (pageIndexes.TryGetValue(placement.PageId, out var pageIndex))
            {
                placed[function] = (pageIndex, placement.Id);
            }
        }

        _locateIndex = (_version, functions, placed);
        return (functions, placed);
    }

    /// <summary>Yerleşimin cihazı ve function'ları; özellik penceresi bunları gösterir.</summary>
    public DeviceProperties? PropertiesOf(PlacementId placementId)
    {
        if (Project is not { } project
            || project.Placements.FirstOrDefault(placement => placement.Id == placementId) is not { } found)
        {
            return null;
        }

        var device = project.Devices.First(candidate => candidate.Functions.Any(function => function.Id == found.FunctionId));
        var part = device.Part;
        return new DeviceProperties(
            device.Id,
            project.DesignationOf(device).ToString(),
            device.ProductTag,
            device.Description,
            part is null ? null : $"{part.Manufacturer} {part.OrderCode}",
            part?.TypeDesignation,
            [
                .. device.Functions.Select(function =>
                    $"{function.Kind}: {string.Join(", ", function.ConnectionPoints.Select(point => point.Name))}"
                    + (string.IsNullOrEmpty(function.Description) ? string.Empty : $"  ({function.Description})")),
            ]);
    }

    /// <summary>
    /// Çapraz referans bağlantısının (<see cref="Ake.Render.LinkItem"/>) hedef sayfasının sırası; hedef bu projede
    /// değilse <c>null</c>.
    /// </summary>
    public int? PageIndexOfAnchor(string anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (Project is null)
        {
            return null;
        }

        var index = Project.Pages.ToList().FindIndex(page => Ake.Render.SheetRenderer.AnchorOf(page) == anchor);
        return index < 0 ? null : index;
    }

    /// <summary>Cihazın ürün tag'ini değiştirir; yapı düğümleri aynı kalır. Değişmeyen tag geçmişe kayıt düşmez.</summary>
    public Result Rename(DeviceId deviceId, string productTag)
    {
        ArgumentNullException.ThrowIfNull(productTag);

        if (Project?.FindDevice(deviceId) is not { } device)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        var tag = productTag.Trim();
        return tag == device.ProductTag
            ? Result.Success()
            : Execute(new Retag(deviceId, tag, device.FunctionNodeId, device.LocationNodeId));
    }
}
