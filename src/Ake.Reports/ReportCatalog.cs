using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Reports;

/// <summary>Yerleşik raporlar. Her rapor modelden saf bir fonksiyonla üretilir; modeli değiştirmez.</summary>
public static class ReportCatalog
{
    public static IReadOnlyList<string> Names { get; } = ["bom", "cables", "wires", "terminals", "io", "labels"];

    /// <summary>Örnek proje ve makine mühendisliği şablonlarıyla uyumlu özel listeler (ADR-0057).</summary>
    public static IReadOnlyList<string> SpecialNames { get; } = ["plc-xref", "motors", "islands", "controller-io-blocks", "driver-io-blocks"];

    public static Result<ReportTable> Build(string name, Project project)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(project);

        return name switch
        {
            "bom" => Result.Success(Bom(project)),
            "cables" => Result.Success(Cables(project)),
            "wires" => Result.Success(Wires(project)),
            "terminals" => Result.Success(Terminals(project)),
            "io" => Result.Success(Io(project)),
            "labels" => Result.Success(Labels(project)),
            "plc-xref" => Result.Success(PlcCrossReference(project)),
            "motors" => Result.Success(Motors(project)),
            "islands" => Result.Success(Islands(project)),
            "controller-io-blocks" => Result.Success(ControllerIoBlocks(project)),
            "driver-io-blocks" => Result.Success(DriverIoBlocks(project)),
            _ => Result.Failure<ReportTable>(new Error(
                "rapor.yok", $"'{name}' adında rapor yok. Raporlar: {string.Join(", ", Names.Concat(SpecialNames))}")),
        };
    }

    /// <summary>
    /// Malzeme listesi. Ürün bilgisi olan device'lar üretici ve sipariş koduna göre gruplanır;
    /// kablolar tipine göre. Ürün bilgisi olmayanlar eksik veri görünsün diye tek tek listelenir.
    /// </summary>
    public static ReportTable Bom(Project project)
    {
        var rows = new List<IReadOnlyList<object?>>();

        var withPart = project.Devices
            .Where(device => device.Part is not null)
            .GroupBy(device => (device.Part!.Manufacturer, device.Part.OrderCode))
            .OrderBy(group => group.Key.Manufacturer, StringComparer.Ordinal)
            .ThenBy(group => group.Key.OrderCode, StringComparer.Ordinal);
        foreach (var group in withPart)
        {
            rows.Add(BomRow(
                rows.Count + 1,
                group.Count(),
                group.Key.Manufacturer,
                group.Key.OrderCode,
                group.Select(device => device.Part!.TypeDesignation).FirstOrDefault(type => type is not null),
                group.First().Description,
                group.Select(device => project.DesignationOf(device).ToString())));
        }

        var typedCables = project.Cables
            .Where(cable => cable.Type is not null)
            .GroupBy(cable => cable.Type!)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in typedCables)
        {
            rows.Add(BomRow(
                rows.Count + 1, group.Count(), null, null, group.Key, "Kablo", group.Select(cable => $"-{cable.ProductTag}")));
        }

        foreach (var device in project.Devices.Where(device => device.Part is null && device.Kind != DeviceKind.Junction))
        {
            rows.Add(BomRow(
                rows.Count + 1, 1, null, null, null, device.Description, [project.DesignationOf(device).ToString()]));
        }

        foreach (var cable in project.Cables.Where(cable => cable.Type is null))
        {
            rows.Add(BomRow(rows.Count + 1, 1, null, null, null, "Kablo", [$"-{cable.ProductTag}"]));
        }

        return new ReportTable(
            "bom",
            "Malzeme listesi",
            [
                new("pos", "Poz"),
                new("quantity", "Adet"),
                new("manufacturer", "Üretici"),
                new("orderCode", "Sipariş kodu"),
                new("typeDesignation", "Tip"),
                new("description", "Açıklama"),
                new("tags", "Tag"),
            ],
            rows);
    }

    /// <summary>Kablo listesi: damar başına bir satır, iki ucuyla. Kullanılmayan damar boş uçlarla çıkar.</summary>
    public static ReportTable Cables(Project project)
    {
        var byCore = project.Connections
            .Where(connection => connection.CoreId is not null)
            .ToDictionary(connection => connection.CoreId!.Value);

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var cable in project.Cables)
        {
            foreach (var core in cable.Cores)
            {
                var connection = byCore.GetValueOrDefault(core.Id);
                rows.Add(
                [
                    $"-{cable.ProductTag}",
                    cable.Type,
                    core.Designation,
                    core.CrossSectionMm2,
                    core.Color,
                    connection is null ? null : Tag(project, connection.From),
                    connection is null ? null : Tag(project, connection.To),
                ]);
            }
        }

        return new ReportTable(
            "cables",
            "Kablo listesi",
            [
                new("cable", "Kablo"),
                new("type", "Tip"),
                new("core", "Damar"),
                new("crossSection", "Kesit (mm²)"),
                new("color", "Renk"),
                new("from", "Kaynak"),
                new("to", "Hedef"),
            ],
            rows);
    }

    /// <summary>
    /// Bağlantı listesi: her bağlantı bir satır. Tel numarası olanlar numara sırasıyla önce gelir,
    /// kablo damarı ve köprüler model sırasıyla sonra.
    /// </summary>
    public static ReportTable Wires(Project project)
    {
        var connectivity = Connectivity.Compute(project);
        var cores = CoreIndex(project);

        var ordered = project.Connections
            .Select((connection, index) => (connection, index))
            .OrderBy(item => WireOrder(item.connection.Wire?.Number))
            .ThenBy(item => item.index);

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var (connection, _) in ordered)
        {
            var core = connection.CoreId is { } coreId ? cores[coreId] : default;
            rows.Add(
            [
                connection.Wire?.Number,
                connection.Wire is not null ? "Tel" : core.Core is not null ? $"Kablo {CoreLabel(core)}" : "Köprü",
                connection.Wire?.CrossSectionMm2 ?? core.Core?.CrossSectionMm2,
                connection.Wire?.Color ?? core.Core?.Color,
                Tag(project, connection.From),
                Tag(project, connection.To),
                connectivity.PotentialOf(connection.From).Name,
            ]);
        }

        return new ReportTable(
            "wires",
            "Bağlantı listesi",
            [
                new("number", "Tel no"),
                new("medium", "Ortam"),
                new("crossSection", "Kesit (mm²)"),
                new("color", "Renk"),
                new("from", "Kaynak"),
                new("to", "Hedef"),
                new("potential", "Potansiyel"),
            ],
            rows);
    }

    /// <summary>
    /// Klemens planı: klemens başına bir satır. Function'ın ilk bağlantı noktası iç (pano içi),
    /// ikincisi dış (saha) taraftır. Aynı gruptaki başka klemense giden bağlantı köprüdür.
    /// </summary>
    public static ReportTable Terminals(Project project)
    {
        var connectivity = Connectivity.Compute(project);
        var cores = CoreIndex(project);
        var rows = new List<IReadOnlyList<object?>>();

        foreach (var strip in project.Devices.Where(device => device.Kind == DeviceKind.TerminalStrip))
        {
            var ownPoints = strip.Functions
                .SelectMany(function => function.ConnectionPoints.Select(point => (point.Id, function)))
                .ToDictionary(item => item.Id, item => item.function);

            foreach (var terminal in strip.Functions)
            {
                var inner = Side(terminal, 0);
                var outer = Side(terminal, 1);

                rows.Add(
                [
                    project.DesignationOf(strip).ToString(),
                    TerminalName(terminal),
                    inner.Targets,
                    inner.Media,
                    outer.Targets,
                    outer.Media,
                    Join(inner.Jumpers.Concat(outer.Jumpers).Distinct()),
                    terminal.ConnectionPoints.Count > 0
                        ? connectivity.PotentialOf(terminal.ConnectionPoints[0].Id).Name
                        : null,
                ]);
            }

            // Bir tarafın (0 iç, 1 dış) bütün bağlantıları; çok bağlantılı klemenste taraftaki bütün noktalar.
            (string? Targets, string? Media, List<string> Jumpers) Side(Function terminal, int side)
            {
                var targets = new List<string>();
                var media = new List<string>();
                var jumpers = new List<string>();
                var sidePoints = TerminalNames.OnSide(terminal, side).Select(candidate => candidate.Id).ToHashSet();
                foreach (var connection in project.Connections.Where(c => sidePoints.Contains(c.From) || sidePoints.Contains(c.To)))
                {
                    var point = sidePoints.Contains(connection.From) ? connection.From : connection.To;
                    var other = connection.From == point ? connection.To : connection.From;
                    if (ownPoints.TryGetValue(other, out var neighbour))
                    {
                        jumpers.Add(TerminalName(neighbour));
                        continue;
                    }

                    targets.Add(Tag(project, other)!);
                    if (connection.Wire?.Number is { } number)
                    {
                        media.Add(number);
                    }
                    else if (connection.CoreId is { } coreId)
                    {
                        media.Add(CoreLabel(cores[coreId]));
                    }
                }

                return (Join(targets), Join(media), jumpers);
            }
        }

        return new ReportTable(
            "terminals",
            "Klemens planı",
            [
                new("strip", "Klemens grubu"),
                new("terminal", "Klemens"),
                new("innerTarget", "İç hedef"),
                new("innerWire", "İç tel / damar"),
                new("outerTarget", "Dış hedef"),
                new("outerWire", "Dış tel / damar"),
                new("jumpers", "Köprü"),
                new("potential", "Potansiyel"),
            ],
            rows);
    }

    /// <summary>
    /// PLC I/O listesi: kanal başına bir satır. "Klemens" sinyalin geçtiği klemensler, "Kod" sinyalin
    /// ulaştığı saha elemanıdır; ikisi de kanalın potansiyelinden çıkarılır.
    /// </summary>
    public static ReportTable Io(Project project)
    {
        var connectivity = Connectivity.Compute(project);
        var cores = CoreIndex(project);
        var functions = project.Devices
            .SelectMany(device => device.Functions.Select(function => (device, function)))
            .ToDictionary(item => item.function.Id);

        var channels =
            from controller in project.Controllers
            from network in controller.Networks
            from node in network.Nodes
            from module in node.Modules
            from channel in module.Channels
            select (controller, network, node, module, channel);

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var (controller, network, node, module, channel) in channels)
        {
            var traced = TraceChannel(project, connectivity, cores, functions, channel);
            var moduleDevice = project.FindDevice(module.DeviceId);
            rows.Add(
            [
                controller.Name,
                network.Name,
                node.Name,
                moduleDevice is null ? null : project.DesignationOf(moduleDevice).ToString(),
                module.Position,
                $"{channel.SignalType}{channel.Index.ToString(CultureInfo.InvariantCulture)}",
                channel.SymbolName,
                channel.Description,
                channel.Address,
                traced.Own,
                traced.Terminals,
                traced.Targets,
            ]);
        }

        return new ReportTable(
            "io",
            "PLC I/O listesi",
            [
                new("controller", "Kontrolör"),
                new("network", "Ağ"),
                new("node", "Node"),
                new("module", "Modül"),
                new("position", "Sıra"),
                new("channel", "Nr"),
                new("symbol", "İsim"),
                new("description", "Açıklama"),
                new("address", "Adres"),
                new("point", "Uç"),
                new("terminal", "Klemens"),
                new("target", "Kod"),
            ],
            rows);
    }

    /// <summary>
    /// PLC adresi ↔ tel numarası çapraz referans tablosu (Faz 6c-4, ADR-0056): her PLC kanalının adresi, sembolik adı,
    /// bağlı tel/damar numaraları, klemensi, saha elemanı ucu ve modül ucu; PLC adresine ya da tel numarasına göre sıralanır.
    /// </summary>
    public static ReportTable PlcCrossReference(Project project, bool sortByWire = false)
    {
        ArgumentNullException.ThrowIfNull(project);

        var connectivity = Connectivity.Compute(project);
        var cores = CoreIndex(project);
        var functions = project.Devices
            .SelectMany(device => device.Functions.Select(function => (device, function)))
            .ToDictionary(item => item.function.Id);

        var entries = new List<(string? Address, string? FirstWire, int Order, IReadOnlyList<object?> Row)>();
        var order = 0;
        foreach (var controller in project.Controllers)
        {
            foreach (var network in controller.Networks)
            {
                foreach (var node in network.Nodes)
                {
                    foreach (var module in node.Modules)
                    {
                        var moduleDevice = project.FindDevice(module.DeviceId);
                        var moduleTag = moduleDevice is null ? null : project.DesignationOf(moduleDevice).ToString();
                        foreach (var channel in module.Channels)
                        {
                            var traced = TraceChannel(project, connectivity, cores, functions, channel);
                            var row = new object?[]
                            {
                                channel.Address,
                                channel.SymbolName,
                                $"{channel.SignalType}{channel.Index.ToString(CultureInfo.InvariantCulture)}",
                                traced.Wires,
                                traced.Terminals,
                                traced.Targets,
                                traced.Own,
                                moduleTag,
                                channel.Description,
                            };
                            entries.Add((channel.Address, traced.FirstWire, order++, row));
                        }
                    }
                }
            }
        }

        var sorted = sortByWire
            ? entries.OrderBy(item => WireOrder(item.FirstWire)).ThenBy(item => item.Order)
            : entries.OrderBy(item => string.IsNullOrEmpty(item.Address) ? 1 : 0).ThenBy(item => item.Address ?? string.Empty, StringComparer.Ordinal).ThenBy(item => item.Order);

        return new ReportTable(
            "plc-xref",
            "PLC adresi - Tel çapraz referansı",
            [
                new("address", "PLC adresi"),
                new("symbol", "Sembolik ad"),
                new("signal", "Sinyal"),
                new("wire", "Tel / damar no"),
                new("terminal", "Klemens"),
                new("target", "Saha elemanı ucu"),
                new("point", "Modül ucu"),
                new("module", "Modül"),
                new("description", "Açıklama"),
            ],
            [.. sorted.Select(item => item.Row)]);
    }

    /// <summary>
    /// Motor ve redüktör listesi (ADR-0057): sürücü ismi/tipi, Sercos/CANopen adresleri, motor tipi, redüktör, güç (kW) ve metraj.
    /// </summary>
    public static ReportTable Motors(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var connectivity = Connectivity.Compute(project);
        var rows = new List<IReadOnlyList<object?>>();
        var targets = project.Devices
            .Where(device => device.Kind != DeviceKind.Junction
                && (device.Functions.Any(fn => fn.Kind == FunctionKind.Motor)
                    || device.Properties.ContainsKey("motorType")
                    || device.Properties.ContainsKey("driverType")
                    || device.Properties.ContainsKey("sercosAddress")
                    || device.Properties.ContainsKey("canOpenAddress")))
            .ToList();

        foreach (var device in targets)
        {
            var props = device.Properties;
            var connectedDriver = FindConnectedDriver(project, connectivity, device);
            var driverName = props.GetValueOrDefault("driverName")
                ?? (connectedDriver is not null ? connectedDriver.ProductTag : (props.ContainsKey("driverType") || props.ContainsKey("sercosAddress") || props.ContainsKey("canOpenAddress") ? device.ProductTag : null));
            var driverType = props.GetValueOrDefault("driverType")
                ?? connectedDriver?.Part?.TypeDesignation
                ?? connectedDriver?.Part?.OrderCode
                ?? (props.ContainsKey("motorType") ? null : device.Part?.TypeDesignation ?? device.Part?.OrderCode);
            var sercos = props.GetValueOrDefault("sercosAddress")
                ?? connectedDriver?.Properties.GetValueOrDefault("sercosAddress")
                ?? (!props.ContainsKey("canOpenAddress") ? connectedDriver?.Address ?? device.Address : null);
            var canOpen = props.GetValueOrDefault("canOpenAddress")
                ?? connectedDriver?.Properties.GetValueOrDefault("canOpenAddress");
            var axisName = props.GetValueOrDefault("axisName") ?? device.Description ?? device.Functions.FirstOrDefault()?.Description;
            var motor = props.GetValueOrDefault("motorType")
                ?? (device.Functions.Any(fn => fn.Kind == FunctionKind.Motor) ? device.Part?.TypeDesignation ?? device.Part?.OrderCode : null);
            var gearbox = props.GetValueOrDefault("gearbox");
            var kw = props.GetValueOrDefault("powerKw") ?? props.GetValueOrDefault("ratedPower");
            var length = props.GetValueOrDefault("cableLength");

            rows.Add([driverName, driverType, sercos, canOpen, axisName, motor, gearbox, kw, length]);
        }

        return new ReportTable(
            "motors",
            "Motor ve redüktör listesi",
            [
                new("driverName", "Sürücü İsmi"),
                new("driverType", "Sürücü Tipi"),
                new("sercosAddress", "Sercos Adr"),
                new("canOpenAddress", "Can Open Adr"),
                new("name", "İsim"),
                new("motor", "Kullanılan Motorlar"),
                new("gearbox", "Redüktör"),
                new("kw", "KW"),
                new("length", "Metraj"),
            ],
            rows);
    }

    private static Device? FindConnectedDriver(Project project, Connectivity connectivity, Device motor)
    {
        if (!motor.Functions.Any(fn => fn.Kind == FunctionKind.Motor))
        {
            return null;
        }

        var points = motor.Functions.SelectMany(fn => fn.ConnectionPoints).Select(pt => pt.Id).ToHashSet();
        foreach (var point in points)
        {
            var net = connectivity.NetOf(point);
            foreach (var candidatePoint in net.Points)
            {
                if (points.Contains(candidatePoint) || project.Locate(candidatePoint) is not { } location)
                {
                    continue;
                }

                var dev = location.Device;
                if (dev.Id != motor.Id
                    && (dev.Properties.ContainsKey("driverType")
                        || dev.Properties.ContainsKey("sercosAddress")
                        || dev.Properties.ContainsKey("canOpenAddress")
                        || dev.Address is not null))
                {
                    return dev;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Ada listesi (ADR-0057): valf adaları, sensör portları, lokasyon, sinyal adı/etiketi ve eleman tipi.
    /// </summary>
    public static ReportTable Islands(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var rows = new List<IReadOnlyList<object?>>();
        var explicitItems = project.Devices
            .Where(device => device.Kind != DeviceKind.Junction
                && (device.Properties.ContainsKey("island")
                    || device.Properties.ContainsKey("port")
                    || device.Properties.ContainsKey("signalName")
                    || device.Properties.ContainsKey("signalLabel")))
            .ToList();

        foreach (var device in explicitItems)
        {
            var props = device.Properties;
            var loc = props.GetValueOrDefault("islandLocation")
                ?? project.StructurePath(device.LocationNodeId);
            rows.Add(
            [
                props.GetValueOrDefault("island") ?? device.ProductTag,
                loc,
                props.GetValueOrDefault("moduleType") ?? device.Part?.TypeDesignation,
                props.GetValueOrDefault("port"),
                props.GetValueOrDefault("signalName") ?? device.Description,
                props.GetValueOrDefault("signalLabel") ?? project.DesignationOf(device).ToString(),
                props.GetValueOrDefault("elementType") ?? device.Part?.OrderCode ?? device.Part?.TypeDesignation,
            ]);
        }

        if (rows.Count == 0)
        {
            var connectivity = Connectivity.Compute(project);
            var cores = CoreIndex(project);
            var functions = project.Devices
                .SelectMany(device => device.Functions.Select(function => (device, function)))
                .ToDictionary(item => item.function.Id);

            foreach (var controller in project.Controllers)
            {
                foreach (var network in controller.Networks)
                {
                    foreach (var node in network.Nodes)
                    {
                        var nodeDevice = project.FindDevice(node.DeviceId);
                        var location = nodeDevice is null ? null : project.StructurePath(nodeDevice.LocationNodeId);
                        foreach (var module in node.Modules)
                        {
                            var moduleDevice = project.FindDevice(module.DeviceId);
                            var moduleType = moduleDevice?.Part?.TypeDesignation ?? moduleDevice?.Part?.OrderCode ?? moduleDevice?.Description;
                            foreach (var channel in module.Channels)
                            {
                                var traced = TraceChannel(project, connectivity, cores, functions, channel);
                                rows.Add(
                                [
                                    node.Name,
                                    location,
                                    moduleType,
                                    $"{channel.SignalType}{channel.Index.ToString(CultureInfo.InvariantCulture)}",
                                    channel.SymbolName ?? channel.Description,
                                    traced.Targets,
                                    traced.Terminals,
                                ]);
                            }
                        }
                    }
                }
            }
        }

        return new ReportTable(
            "islands",
            "Ada listesi",
            [
                new("island", "Ada"),
                new("location", "Lokasyon"),
                new("moduleType", "Modül Tipi"),
                new("port", "Port"),
                new("signalName", "Sinyal Adı"),
                new("signalLabel", "Sinyal Etiketi"),
                new("elementType", "Eleman Tipi"),
            ],
            rows);
    }

    /// <summary>
    /// Kontrolör I/O yan yana blok listesi (ADR-0057): her modül 4 sütunlu (Klemens | Nr | İsim | Kod) yan yana blok olarak dizilir.
    /// </summary>
    public static ReportTable ControllerIoBlocks(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var blocks = BuildIoModuleBlocks(project, driversOnly: false);
        return BuildSideBySideReport(
            "controller-io-blocks",
            "Kontrolör I/O blok listesi",
            ["Klemens", "Nr", "İsim", "Kod"],
            blocks,
            row => [row.Terminal, row.Nr, row.Name, row.Code]);
    }

    /// <summary>
    /// Sürücü I/O yan yana blok listesi (ADR-0057): her sürücü/modül 4 sütunlu (Nr | İsim | Kod | Klemens) yan yana blok olarak dizilir.
    /// </summary>
    public static ReportTable DriverIoBlocks(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var blocks = BuildIoModuleBlocks(project, driversOnly: true);
        return BuildSideBySideReport(
            "driver-io-blocks",
            "Sürücü I/O blok listesi",
            ["Nr", "İsim", "Kod", "Klemens"],
            blocks,
            row => [row.Nr, row.Name, row.Code, row.Terminal]);
    }

    private sealed record IoBlockRow(string? Terminal, string? Nr, string? Name, string? Code);

    private sealed record IoBlock(string Header, List<IoBlockRow> Rows);

    private static List<IoBlock> BuildIoModuleBlocks(Project project, bool driversOnly)
    {
        var connectivity = Connectivity.Compute(project);
        var cores = CoreIndex(project);
        var functions = project.Devices
            .SelectMany(device => device.Functions.Select(function => (device, function)))
            .ToDictionary(item => item.function.Id);

        var blocks = new List<IoBlock>();
        foreach (var controller in project.Controllers)
        {
            foreach (var network in controller.Networks)
            {
                foreach (var node in network.Nodes)
                {
                    var nodeDevice = project.FindDevice(node.DeviceId);
                    var isDriverNode = node.Name.Contains("LXM", StringComparison.OrdinalIgnoreCase)
                        || node.Name.Contains("Drive", StringComparison.OrdinalIgnoreCase)
                        || node.Name.Contains("Sürücü", StringComparison.OrdinalIgnoreCase)
                        || nodeDevice?.Properties.ContainsKey("driverType") == true;
                    if (driversOnly != isDriverNode && !(driversOnly && node.Modules.Any(m => project.FindDevice(m.DeviceId)?.Properties.ContainsKey("driverIoGroup") == true)))
                    {
                        continue;
                    }

                    foreach (var module in node.Modules)
                    {
                        var moduleDevice = project.FindDevice(module.DeviceId);
                        var isDriverModule = moduleDevice?.Properties.ContainsKey("driverIoGroup") == true || isDriverNode;
                        if (driversOnly != isDriverModule)
                        {
                            continue;
                        }

                        var moduleLabel = moduleDevice is not null
                            ? $"{project.DesignationOf(moduleDevice)}" + (moduleDevice.Part?.TypeDesignation is { } td ? $" ({td})" : string.Empty)
                            : $"{node.Name} Modül {module.Position}";
                        var rows = new List<IoBlockRow>();
                        foreach (var channel in module.Channels)
                        {
                            var traced = TraceChannel(project, connectivity, cores, functions, channel);
                            rows.Add(new IoBlockRow(
                                traced.Terminals ?? moduleDevice?.Properties.GetValueOrDefault($"ch.{channel.Index}.terminal"),
                                $"{channel.SignalType}{channel.Index.ToString(CultureInfo.InvariantCulture)}",
                                channel.SymbolName ?? channel.Description,
                                traced.Targets ?? channel.Description));
                        }

                        blocks.Add(new IoBlock(moduleLabel, rows));
                    }
                }
            }
        }

        if (driversOnly)
        {
            foreach (var device in project.Devices.Where(d => d.Properties.ContainsKey("driverIoGroup") && project.Controllers.SelectMany(c => c.Networks).SelectMany(n => n.Nodes).SelectMany(n => n.Modules).All(m => m.DeviceId != d.Id)))
            {
                var header = device.Properties.GetValueOrDefault("driverIoGroup") ?? device.ProductTag;
                var rows = device.Functions.Select((fn, idx) => new IoBlockRow(
                    device.Properties.GetValueOrDefault($"io.{idx}.terminal"),
                    fn.ConnectionPoints.FirstOrDefault()?.Name ?? $"DI{idx + 1}",
                    fn.Description,
                    device.Properties.GetValueOrDefault($"io.{idx}.code"))).ToList();
                blocks.Add(new IoBlock(header, rows));
            }
        }

        return blocks;
    }

    private static ReportTable BuildSideBySideReport(
        string name,
        string title,
        string[] blockHeaders,
        List<IoBlock> blocks,
        Func<IoBlockRow, object?[]> rowSelector)
    {
        if (blocks.Count == 0)
        {
            return new ReportTable(
                name,
                title,
                [.. blockHeaders.Select((header, idx) => new ReportColumn($"b0c{idx}", header))],
                []);
        }

        var columns = new List<ReportColumn>();
        for (var b = 0; b < blocks.Count; b++)
        {
            if (b > 0)
            {
                columns.Add(new ReportColumn($"sep{b}", string.Empty));
            }

            for (var c = 0; c < blockHeaders.Length; c++)
            {
                columns.Add(new ReportColumn($"b{b}c{c}", blockHeaders[c]));
            }
        }

        var maxRows = blocks.Max(b => b.Rows.Count);
        var rows = new List<IReadOnlyList<object?>>(maxRows + 1);

        var titleRow = new object?[columns.Count];
        for (var b = 0; b < blocks.Count; b++)
        {
            titleRow[b * 5] = blocks[b].Header;
        }

        rows.Add(titleRow);

        for (var r = 0; r < maxRows; r++)
        {
            var row = new object?[columns.Count];
            for (var b = 0; b < blocks.Count; b++)
            {
                if (r < blocks[b].Rows.Count)
                {
                    var cells = rowSelector(blocks[b].Rows[r]);
                    for (var c = 0; c < cells.Length; c++)
                    {
                        row[(b * 5) + c] = cells[c];
                    }
                }
            }

            rows.Add(row);
        }

        return new ReportTable(name, title, columns, rows);
    }

    private static (string? Own, string? FirstWire, string? Wires, string? Terminals, string? Targets) TraceChannel(
        Project project,
        Connectivity connectivity,
        Dictionary<CableCoreId, (Cable Cable, CableCore Core)> cores,
        Dictionary<FunctionId, (Device Device, Function Function)> functions,
        Channel channel)
    {
        string? own = null;
        var wires = new List<string>();
        var terminals = new List<string>();
        var targets = new List<string>();

        if (channel.FunctionId is { } functionId
            && functions.TryGetValue(functionId, out var end)
            && end.Function.ConnectionPoints.Count > 0)
        {
            var ownPoints = end.Function.ConnectionPoints.Select(point => point.Id).ToHashSet();
            var potential = connectivity.PotentialOf(end.Function.ConnectionPoints[0].Id);
            own = Tag(project, end.Function.ConnectionPoints[0].Id);

            var potPoints = potential.Points.ToHashSet();
            foreach (var connection in project.Connections.Where(c => potPoints.Contains(c.From) || potPoints.Contains(c.To)))
            {
                if (connection.Wire?.Number is { } number)
                {
                    wires.Add(number);
                }
                else if (connection.CoreId is { } coreId)
                {
                    wires.Add(CoreLabel(cores[coreId]));
                }
            }

            foreach (var point in potential.Points)
            {
                if (ownPoints.Contains(point) || project.Locate(point) is not { } location)
                {
                    continue;
                }

                if (location.Function.PassThrough)
                {
                    terminals.Add($"{project.DesignationOf(location.Device)}:{TerminalName(location.Function)}");
                }
                else
                {
                    targets.Add(Tag(project, point)!);
                }
            }
        }

        var distinctWires = wires.Distinct().ToList();
        return (own, distinctWires.FirstOrDefault(), Join(distinctWires), Join(terminals.Distinct()), Join(targets));
    }

    private static IReadOnlyList<object?> BomRow(
        int pos,
        int quantity,
        string? manufacturer,
        string? orderCode,
        string? type,
        string? description,
        IEnumerable<string> tags) =>
        [pos, quantity, manufacturer, orderCode, type, description, Join(tags)];

    /// <summary>
    /// Kablo ve tel etiketleri (ADR-0043): her tel ya da kablo damarı için iki satır, her uç için bir etiket. "Etiket" iletkenin kimliğidir (tel numarası ya da
    /// kablo:damar), "Bu uç" etiketin takıldığı bağlantı noktası, "Karşı uç" gidilen yerdir; böylece iki ucundan da izlenebilir. Etiket yazıcı
    /// yazılımları (CSV içe aktaran) satır başına bir etiket basar. Numarasız köprüler etiketlenmez.
    /// </summary>
    public static ReportTable Labels(Project project)
    {
        var cores = CoreIndex(project);
        var ordered = project.Connections
            .Select((connection, index) => (connection, index))
            .Where(item => item.connection.Wire?.Number is not null || item.connection.CoreId is not null)
            .OrderBy(item => item.connection.Wire?.Number is { } number ? WireOrder(number) : (3, 0, string.Empty))
            .ThenBy(item => item.index);

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var (connection, _) in ordered)
        {
            var core = connection.CoreId is { } coreId ? cores[coreId] : default;
            var label = connection.Wire?.Number ?? CoreLabel(core);
            var color = connection.Wire?.Color ?? core.Core?.Color;
            var crossSection = connection.Wire?.CrossSectionMm2 ?? core.Core?.CrossSectionMm2;
            var ends = new[] { (connection.From, connection.To, "A"), (connection.To, connection.From, "B") };
            foreach (var (here, there, end) in ends)
            {
                rows.Add([label, end, Tag(project, here), Tag(project, there), color, crossSection]);
            }
        }

        return new ReportTable(
            "labels",
            "Kablo ve tel etiketleri",
            [
                new("label", "Etiket"),
                new("end", "Uç"),
                new("here", "Bu uç"),
                new("there", "Karşı uç"),
                new("color", "Renk"),
                new("crossSection", "Kesit (mm²)"),
            ],
            rows);
    }

    private static Dictionary<CableCoreId, (Cable Cable, CableCore Core)> CoreIndex(Project project) =>
        project.Cables
            .SelectMany(cable => cable.Cores.Select(core => (cable, core)))
            .ToDictionary(item => item.core.Id, item => (item.cable, item.core));

    private static string CoreLabel((Cable Cable, CableCore Core) core) =>
        $"-{core.Cable.ProductTag}:{core.Core.Designation}";

    private static string TerminalName(Function terminal)
    {
        var name = terminal.Description ?? terminal.ConnectionPoints.FirstOrDefault()?.Name ?? string.Empty;
        name = terminal.Level > 1 ? $"{name} ({terminal.Level}. kat)" : name;
        return terminal.Role switch
        {
            TerminalRole.ProtectiveEarth => $"{name} (PE)",
            TerminalRole.EndCover => $"{name} (sonlandırıcı)",
            _ => name,
        };
    }

    private static string? Tag(Project project, ConnectionPointId point) => project.DesignationOf(point)?.ToString();

    private static string? Join(IEnumerable<string> values)
    {
        var text = string.Join(", ", values);
        return text.Length == 0 ? null : text;
    }

    // Sayısal numaralar sayı sırasıyla, diğerleri alfabetik, numarasızlar en sonda.
    private static (int Group, long Number, string Text) WireOrder(string? number) =>
        number is null ? (2, 0, string.Empty)
        : long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? (0, value, string.Empty)
        : (1, 0, number);
}
