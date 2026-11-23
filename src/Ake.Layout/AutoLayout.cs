using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Layout;

/// <summary>
/// Otomatik yerleşim, sürüm 0. Henüz ana görünümü olmayan function'ları, fonksiyon düğümü sayfanınkiyle
/// aynı olan sayfaya yerleştirir. Modeli değiştirmez; <see cref="AddPlacement"/> command'larından
/// oluşan bir transaction üretir.
/// </summary>
/// <remarks>
/// Kural: cihazlar beslemeden uzaklıklarına göre satırlara dizilir (besleme üstte, yük altta); bir
/// cihazın kutupları yan yana ve sabit adımla durur, böylece alt alta gelen cihazların kutupları
/// hizalanır. İki uçlu bir sembolün ilk ucu aşağıdaki bir cihaza gidiyorsa sembol baş aşağı çevrilir.
/// Devre tipini tanımaz.
/// </remarks>
public static class AutoLayout
{
    private const double LeftMm = 20;
    private const double TopMm = 30;
    private const double RowPitchMm = 48;
    private const double DeviceGapMm = 14;
    private const double RightMarginMm = 10;

    public static Transaction Build(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var deviceOf = project.Devices
            .SelectMany(device => device.Functions
                .SelectMany(function => function.ConnectionPoints)
                .Select(point => (point.Id, device.Id)))
            .ToDictionary(item => item.Item1, item => item.Item2);
        var ranks = Ranks(project, deviceOf);

        var otherEnds = new Dictionary<ConnectionPointId, List<DeviceId>>();
        foreach (var connection in project.Connections)
        {
            Ends(connection.From).Add(deviceOf[connection.To]);
            Ends(connection.To).Add(deviceOf[connection.From]);
        }

        // Her function'ın çizileceği fonksiyon düğümü. Olağan durumda cihazınınkidir. Ara klemens ise
        // (iki ucu da başka bir düğümün cihazlarına giden klemens) o cihazların sayfasına çizilir;
        // yoksa tel o sayfada kopuk görünür ve klemens ancak çapraz referanstan anlaşılır.
        var nodeOfDevice = project.Devices.ToDictionary(device => device.Id, device => device.FunctionNodeId);
        var homeNode = new Dictionary<FunctionId, StructureNodeId?>();
        foreach (var device in project.Devices)
        {
            foreach (var function in device.Functions)
            {
                homeNode[function.Id] = ThroughNode(function, device) ?? device.FunctionNodeId;
            }
        }

        var placed = project.Placements
            .Where(placement => placement.Role == PlacementRole.Main)
            .Select(placement => placement.FunctionId)
            .ToHashSet();
        var commands = new List<ICommand>();

        foreach (var page in project.Pages)
        {
            if (page.FunctionNodeId is null || project.TemplateFor(page) is not { } template)
            {
                continue;
            }

            var width = SheetGeometry.For(template).DrawingArea.Width - RightMarginMm;
            bool OnThisPage(Function function) =>
                !placed.Contains(function.Id) && homeNode[function.Id] == page.FunctionNodeId;

            var rows = project.Devices
                .Where(device => device.Functions.Any(OnThisPage))
                .GroupBy(device => Rank(device.Id))
                .OrderBy(row => row.Key);

            // Sayfada daha önce yerleşmiş semboller varsa yenileri onların altından başlar; yoksa üst üste binerler.
            var existing = project.Placements.Where(placement => placement.PageId == page.Id).ToList();
            var y = existing.Count == 0 ? TopMm : existing.Max(placement => placement.Y) + RowPitchMm;
            foreach (var row in rows)
            {
                var x = LeftMm;
                foreach (var device in row)
                {
                    foreach (var function in InDrawingOrder(device).Where(OnThisPage))
                    {
                        var symbol = SymbolLibrary.For(function, project);
                        var origin = SnapUp(x - symbol.MinX);
                        if (origin + symbol.MaxX > width && x > LeftMm)
                        {
                            y += RowPitchMm;
                            x = LeftMm;
                            origin = SnapUp(x - symbol.MinX);
                        }

                        // Baş aşağı sembolün başlangıç noktası (ilk ucu) alttadır.
                        var flipped = ShouldFlip(function, symbol, Rank(device.Id));
                        commands.Add(new AddPlacement(new Placement(
                            PlacementId.New(),
                            page.Id,
                            function.Id,
                            PlacementRole.Main,
                            origin,
                            flipped ? y + Symbol.Length : y,
                            flipped ? 180 : 0)));
                        placed.Add(function.Id);
                        x = origin + symbol.MaxX + 3;
                    }

                    x += DeviceGapMm;
                }

                y += RowPitchMm;
            }
        }

        return new Transaction("Otomatik yerleşim", commands);

        int Rank(DeviceId id) => ranks.GetValueOrDefault(id, int.MaxValue);

        List<DeviceId> Ends(ConnectionPointId point)
        {
            if (!otherEnds.TryGetValue(point, out var list))
            {
                list = [];
                otherEnds[point] = list;
            }

            return list;
        }

        StructureNodeId? ThroughNode(Function function, Device device)
        {
            if (!function.PassThrough || function.ConnectionPoints.Count != 2)
            {
                return null;
            }

            StructureNodeId? shared = null;
            foreach (var point in function.ConnectionPoints)
            {
                var nodes = (otherEnds.GetValueOrDefault(point.Id) ?? [])
                    .Where(other => other != device.Id)
                    .Select(other => nodeOfDevice[other])
                    .Distinct()
                    .ToList();
                if (nodes.Count != 1 || nodes[0] is null || nodes[0] == device.FunctionNodeId
                    || (shared is not null && shared != nodes[0]))
                {
                    return null;
                }

                shared = nodes[0];
            }

            return shared;
        }

        // İlk uç üstte durur. İlk ucun bağlantıları alttaki satırlara, ikincininki üsttekilere
        // gidiyorsa sembol çevrilir; yoksa tel sembolün çevresinden dolaşmak zorunda kalır.
        bool ShouldFlip(Function function, Symbol symbol, int ownRank)
        {
            if (function.ConnectionPoints.Count != 2 || symbol.Pins.Count != 2 || symbol.Pins[0].DirectionY >= 0)
            {
                return false;
            }

            var score = 0;
            foreach (var other in otherEnds.GetValueOrDefault(function.ConnectionPoints[0].Id) ?? [])
            {
                score += Math.Sign(Rank(other).CompareTo(ownRank));
            }

            foreach (var other in otherEnds.GetValueOrDefault(function.ConnectionPoints[1].Id) ?? [])
            {
                score -= Math.Sign(Rank(other).CompareTo(ownRank));
            }

            return score > 0;
        }
    }

    // Kutuplar önce: böylece farklı cihazların kutupları aynı kolonlara düşer.
    private static IEnumerable<Function> InDrawingOrder(Device device) =>
        device.Functions.OrderBy(function =>
            function.Kind is FunctionKind.PowerContact or FunctionKind.Protection or FunctionKind.Motor ? 0 : 1);

    private static double SnapUp(double value) => Math.Ceiling(value / Symbol.Pitch) * Symbol.Pitch;

    /// <summary>
    /// Her cihazın besleme girişine uzaklığı (bağlantı sayısı). Faz işaretleri başlangıçtır; PE ve nötr
    /// üzerindeki bağlantılar sayılmaz, yoksa her şey topraktan kısa yoldan birbirine bağlanır.
    /// </summary>
    private static Dictionary<DeviceId, int> Ranks(Project project, Dictionary<ConnectionPointId, DeviceId> deviceOf)
    {
        var connectivity = Connectivity.Compute(project);
        var ignored = project.PotentialSources
            .Where(source => source.Kind is PotentialKind.ProtectiveEarth or PotentialKind.Neutral)
            .Select(source => connectivity.PotentialOf(source.At))
            .ToHashSet(ReferenceEqualityComparer.Instance);

        var neighbours = new Dictionary<DeviceId, List<DeviceId>>();
        foreach (var connection in project.Connections)
        {
            if (ignored.Contains(connectivity.PotentialOf(connection.From)))
            {
                continue;
            }

            var a = deviceOf[connection.From];
            var b = deviceOf[connection.To];
            if (a != b)
            {
                Neighbours(a).Add(b);
                Neighbours(b).Add(a);
            }
        }

        var phases = project.PotentialSources.Where(source => source.Kind == PotentialKind.Phase).ToList();
        var starts = (phases.Count > 0 ? phases : project.PotentialSources.ToList())
            .Select(source => deviceOf[source.At])
            .Distinct();

        var ranks = new Dictionary<DeviceId, int>();
        var queue = new Queue<DeviceId>();
        foreach (var start in starts)
        {
            ranks[start] = 0;
            queue.Enqueue(start);
        }

        while (queue.TryDequeue(out var device))
        {
            foreach (var next in neighbours.GetValueOrDefault(device) ?? [])
            {
                if (ranks.TryAdd(next, ranks[device] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return ranks;

        List<DeviceId> Neighbours(DeviceId id)
        {
            if (!neighbours.TryGetValue(id, out var list))
            {
                list = [];
                neighbours[id] = list;
            }

            return list;
        }
    }
}
