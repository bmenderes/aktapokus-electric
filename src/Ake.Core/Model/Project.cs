namespace Ake.Core.Model;

/// <summary>Bir bağlantı noktasının modeldeki yeri.</summary>
public sealed record ConnectionPointLocation(Device Device, Function Function, ConnectionPoint Point);

/// <summary>
/// Elektriksel modelin kökü ve tek doğruluk kaynağı. Dışarıdan yalnızca okunur; bütün olarak
/// <see cref="Create"/> ile kurulur (dosyadan yükleme) ve Faz 2'den itibaren Command'larla değişir.
/// </summary>
public sealed class Project
{
    private readonly List<StructureNode> _structure;
    private readonly List<Device> _devices;
    private readonly List<Connection> _connections;
    private readonly List<Cable> _cables;
    private readonly List<PotentialSource> _potentialSources;
    private readonly List<Controller> _controllers;
    private readonly List<Page> _pages;
    private readonly List<Placement> _placements;
    private readonly List<SheetTemplate> _sheetTemplates;
    private readonly List<SymbolDefinition> _symbols;
    private readonly List<PageGraphic> _graphics;
    private readonly List<ConnectionKind> _connectionKinds;
    private readonly List<Revision> _revisions;
    private readonly SortedDictionary<string, string> _properties;

    private readonly Dictionary<StructureNodeId, StructureNode> _nodesById = [];
    private readonly Dictionary<ConnectionPointId, ConnectionPointLocation> _pointsById = [];

    private Project(
        ProjectId id,
        string name,
        IEnumerable<StructureNode>? structure,
        IEnumerable<Device>? devices,
        IEnumerable<Connection>? connections,
        IEnumerable<Cable>? cables,
        IEnumerable<PotentialSource>? potentialSources,
        IEnumerable<Controller>? controllers,
        IEnumerable<Page>? pages,
        IEnumerable<Placement>? placements,
        IEnumerable<SheetTemplate>? sheetTemplates,
        IEnumerable<KeyValuePair<string, string>>? properties,
        IEnumerable<SymbolDefinition>? symbols,
        IEnumerable<PageGraphic>? graphics,
        IEnumerable<ConnectionKind>? connectionKinds,
        IEnumerable<Revision>? revisions)
    {
        Id = id;
        Name = name;
        _structure = [.. structure ?? []];
        _devices = [.. devices ?? []];
        _connections = [.. connections ?? []];
        _cables = [.. cables ?? []];
        _potentialSources = [.. potentialSources ?? []];
        _controllers = [.. controllers ?? []];
        _pages = [.. pages ?? []];
        _placements = [.. placements ?? []];
        _sheetTemplates = [.. sheetTemplates ?? []];
        _symbols = [.. symbols ?? []];
        _graphics = [.. graphics ?? []];
        _connectionKinds = [.. connectionKinds ?? []];
        _revisions = [.. revisions ?? []];
        _properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in properties ?? [])
        {
            _properties[key] = value;
        }

        foreach (var node in _structure)
        {
            _nodesById.TryAdd(node.Id, node);
        }

        foreach (var device in _devices)
        {
            foreach (var function in device.Functions)
            {
                foreach (var point in function.ConnectionPoints)
                {
                    _pointsById.TryAdd(point.Id, new ConnectionPointLocation(device, function, point));
                }
            }
        }
    }

    public ProjectId Id { get; }

    public string Name { get; internal set; }

    public IReadOnlyList<StructureNode> Structure => _structure;

    public IReadOnlyList<Device> Devices => _devices;

    public IReadOnlyList<Connection> Connections => _connections;

    public IReadOnlyList<Cable> Cables => _cables;

    public IReadOnlyList<PotentialSource> PotentialSources => _potentialSources;

    public IReadOnlyList<Controller> Controllers => _controllers;

    public IReadOnlyList<Page> Pages => _pages;

    public IReadOnlyList<Placement> Placements => _placements;

    /// <summary>Sayfalardaki serbest grafikler (içe aktarılan çizimler, notlar); elektriksel anlamı yoktur.</summary>
    public IReadOnlyList<PageGraphic> Graphics => _graphics;

    /// <summary>Bağlantı türleri (renk ve çizgi biçimi; ADR-0036).</summary>
    public IReadOnlyList<ConnectionKind> ConnectionKinds => _connectionKinds;

    public ConnectionKind? FindConnectionKind(ConnectionKindId id) => _connectionKinds.FirstOrDefault(kind => kind.Id == id);

    /// <summary>Revizyon geçmişi, eskiden yeniye (ADR-0050).</summary>
    public IReadOnlyList<Revision> Revisions => _revisions;

    internal List<Revision> RevisionList => _revisions;

    public IReadOnlyList<SheetTemplate> SheetTemplates => _sheetTemplates;

    /// <summary>Projeye ait kullanıcı sembolleri (blok editörü, içe aktarılan kütüphaneler).</summary>
    public IReadOnlyList<SymbolDefinition> Symbols => _symbols;

    public SymbolDefinition? FindSymbol(SymbolId id) => _symbols.FirstOrDefault(symbol => symbol.Id == id);

    /// <summary>
    /// Projeye ait serbest alanlar (çizim no, revizyon, müşteri…); antetteki <c>{project.anahtar}</c>
    /// yer tutucuları buradan dolar. Anahtara göre sıralıdır.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties => _properties;

    /// <summary>Device tag'lerinin numaralandırma şeması (ADR-0021).</summary>
    public TagScheme TagScheme { get; internal set; }

    /// <summary>Tel numaralandırma ayarları: kod, başlangıç, adım, basamak, şema (ADR-0041).</summary>
    public WireNumberSettings WireNumbering { get; internal set; } = new();

    /// <summary>Sayfanın şablonu: kendi şablonu, yoksa projenin ilk şablonu, o da yoksa <c>null</c>.</summary>
    public SheetTemplate? TemplateFor(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.TemplateId is { } id
            ? _sheetTemplates.FirstOrDefault(template => template.Id == id)
            : _sheetTemplates.FirstOrDefault();
    }

    /// <summary>
    /// Modeli bütün olarak kurar ve iç tutarlılığını doğrular: kimlikler tekil, bütün referanslar
    /// çözülebilir. Mühendislik kuralları (açık uç, tekrarlanan tag, kısa devre) burada değil,
    /// ERC'de denetlenir.
    /// </summary>
    public static Result<Project> Create(
        ProjectId id,
        string name,
        IEnumerable<StructureNode>? structure = null,
        IEnumerable<Device>? devices = null,
        IEnumerable<Connection>? connections = null,
        IEnumerable<Cable>? cables = null,
        IEnumerable<PotentialSource>? potentialSources = null,
        IEnumerable<Controller>? controllers = null,
        IEnumerable<Page>? pages = null,
        IEnumerable<Placement>? placements = null,
        IEnumerable<SheetTemplate>? sheetTemplates = null,
        IEnumerable<KeyValuePair<string, string>>? properties = null,
        IEnumerable<SymbolDefinition>? symbols = null,
        TagScheme tagScheme = TagScheme.LetterNumber,
        IEnumerable<PageGraphic>? graphics = null,
        IEnumerable<ConnectionKind>? connectionKinds = null,
        WireNumberSettings? wireNumbering = null,
        IEnumerable<Revision>? revisions = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        var project = new Project(
            id,
            name,
            structure,
            devices,
            connections,
            cables,
            potentialSources,
            controllers,
            pages,
            placements,
            sheetTemplates,
            properties,
            symbols,
            graphics,
            connectionKinds,
            revisions);
        project.TagScheme = tagScheme;
        project.WireNumbering = wireNumbering ?? new WireNumberSettings();

        var error = project.Validate();
        return error is null ? Result.Success(project) : Result.Failure<Project>(error);
    }

    public ConnectionPointLocation? Locate(ConnectionPointId id) => _pointsById.GetValueOrDefault(id);

    /// <summary>Düğümün kökten itibaren yolu, nokta ile ayrılmış (A1.M1).</summary>
    public string? StructurePath(StructureNodeId? id)
    {
        if (id is not { } nodeId)
        {
            return null;
        }

        var names = new Stack<string>();
        var node = _nodesById.GetValueOrDefault(nodeId);
        while (node is not null)
        {
            names.Push(node.Name);
            node = node.ParentId is { } parentId ? _nodesById.GetValueOrDefault(parentId) : null;
        }

        return names.Count == 0 ? null : string.Join('.', names);
    }

    public ReferenceDesignation DesignationOf(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return new ReferenceDesignation(
            StructurePath(device.FunctionNodeId),
            StructurePath(device.LocationNodeId),
            device.ProductTag);
    }

    public ReferenceDesignation? DesignationOf(ConnectionPointId id) =>
        Locate(id) is { } location
            ? DesignationOf(location.Device) with { ConnectionPoint = location.Point.Name }
            : null;

    public Device? FindDevice(DeviceId id) => _devices.FirstOrDefault(device => device.Id == id);

    public Connection? FindConnection(ConnectionId id) =>
        _connections.FirstOrDefault(connection => connection.Id == id);

    /// <summary>Bütün ağlardaki bütün PLC kanalları, model sırasıyla.</summary>
    public IEnumerable<Channel> Channels => _controllers
        .SelectMany(controller => controller.Networks)
        .SelectMany(network => network.Nodes)
        .SelectMany(node => node.Modules)
        .SelectMany(module => module.Channels);

    /// <summary>
    /// <see cref="Create"/> ile aynı iç tutarlılık denetimi. Her Command'dan sonra başarılı
    /// olmalıdır; testler bunu doğrular.
    /// </summary>
    public Result CheckIntegrity() => Validate() is { } error ? Result.Failure(error) : Result.Success();

    // ---- Command'ların kullandığı değiştirme üyeleri. Core dışından erişilemez (ADR-0003).

    internal List<Connection> ConnectionList => _connections;

    internal List<PotentialSource> PotentialSourceList => _potentialSources;

    internal List<Placement> PlacementList => _placements;

    internal List<PageGraphic> GraphicList => _graphics;

    internal List<ConnectionKind> ConnectionKindList => _connectionKinds;

    internal List<Page> PageList => _pages;

    internal List<SheetTemplate> SheetTemplateList => _sheetTemplates;

    internal List<SymbolDefinition> SymbolList => _symbols;

    internal List<Cable> CableList => _cables;

    internal List<Controller> ControllerList => _controllers;

    internal void AddNode(StructureNode node)
    {
        _structure.Add(node);
        _nodesById.TryAdd(node.Id, node);
    }

    internal void RemoveNode(StructureNode node)
    {
        _structure.Remove(node);
        _nodesById.Remove(node.Id);
    }

    internal SortedDictionary<string, string> PropertyMap => _properties;

    internal HashSet<Guid> IdSet() => [.. AllIds()];

    internal HashSet<CableCoreId> CoreIds() => [.. _cables.SelectMany(cable => cable.Cores).Select(core => core.Id)];

    internal void InsertDevice(int index, Device device)
    {
        _devices.Insert(index, device);
        foreach (var function in device.Functions)
        {
            foreach (var point in function.ConnectionPoints)
            {
                _pointsById[point.Id] = new ConnectionPointLocation(device, function, point);
            }
        }
    }

    internal void InsertFunction(Device device, int index, Function function)
    {
        device.FunctionList.Insert(index, function);
        foreach (var point in function.ConnectionPoints)
        {
            _pointsById[point.Id] = new ConnectionPointLocation(device, function, point);
        }
    }

    internal int RemoveFunction(Device device, Function function)
    {
        var index = device.FunctionList.IndexOf(function);
        device.FunctionList.RemoveAt(index);
        foreach (var point in function.ConnectionPoints)
        {
            _pointsById.Remove(point.Id);
        }

        return index;
    }

    internal int RemoveDevice(Device device)
    {
        var index = _devices.IndexOf(device);
        _devices.RemoveAt(index);
        foreach (var point in device.Functions.SelectMany(function => function.ConnectionPoints))
        {
            _pointsById.Remove(point.Id);
        }

        return index;
    }

    private Error? Validate() =>
        FindDuplicateId()
        ?? ValidateStructure()
        ?? ValidateDevices()
        ?? ValidateConnections()
        ?? ValidatePotentialSources()
        ?? ValidatePlc()
        ?? ValidatePages()
        ?? ValidateGraphics()
        ?? WireNumbering.Check()
        ?? ValidateConnectionKinds();

    private Error? FindDuplicateId()
    {
        var seen = new HashSet<Guid>();
        foreach (var id in AllIds())
        {
            if (!seen.Add(id))
            {
                return new Error("id.tekrar", $"Aynı kimlik birden çok nesnede kullanılmış: {id}");
            }
        }

        return null;
    }

    private IEnumerable<Guid> AllIds()
    {
        yield return Id.Value;

        foreach (var node in _structure)
        {
            yield return node.Id.Value;
        }

        foreach (var device in _devices)
        {
            yield return device.Id.Value;
            foreach (var function in device.Functions)
            {
                yield return function.Id.Value;
                foreach (var point in function.ConnectionPoints)
                {
                    yield return point.Id.Value;
                }
            }
        }

        foreach (var connection in _connections)
        {
            yield return connection.Id.Value;
            if (connection.Wire is not null)
            {
                yield return connection.Wire.Id.Value;
            }
        }

        foreach (var cable in _cables)
        {
            yield return cable.Id.Value;
            foreach (var core in cable.Cores)
            {
                yield return core.Id.Value;
            }
        }

        foreach (var source in _potentialSources)
        {
            yield return source.Id.Value;
        }

        foreach (var controller in _controllers)
        {
            yield return controller.Id.Value;
            foreach (var network in controller.Networks)
            {
                yield return network.Id.Value;
                foreach (var link in network.PortLinks)
                {
                    yield return link.Id.Value;
                }

                foreach (var node in network.Nodes)
                {
                    yield return node.Id.Value;
                    foreach (var port in node.Ports)
                    {
                        yield return port.Id.Value;
                    }

                    foreach (var module in node.Modules)
                    {
                        yield return module.Id.Value;
                        foreach (var channel in module.Channels)
                        {
                            yield return channel.Id.Value;
                        }
                    }
                }
            }
        }

        foreach (var page in _pages)
        {
            yield return page.Id.Value;
        }

        foreach (var template in _sheetTemplates)
        {
            yield return template.Id.Value;
        }

        foreach (var symbol in _symbols)
        {
            yield return symbol.Id.Value;
        }

        foreach (var placement in _placements)
        {
            yield return placement.Id.Value;
        }

        foreach (var graphic in _graphics)
        {
            yield return graphic.Id.Value;
        }

        foreach (var kind in _connectionKinds)
        {
            yield return kind.Id.Value;
        }
    }

    private Error? ValidateConnectionKinds()
    {
        foreach (var kind in _connectionKinds)
        {
            if (kind.Check() is { } problem)
            {
                return problem;
            }
        }

        if (_connectionKinds.GroupBy(kind => kind.Name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            return new Error("kind.ad_tekrar", $"Aynı adlı birden çok bağlantı türü var: '{duplicate.Key}'.");
        }

        var ids = _connectionKinds.Select(kind => kind.Id).ToHashSet();
        return _connections.FirstOrDefault(connection => connection.KindId is { } id && !ids.Contains(id)) is { } orphan
            ? new Error("kind.yok", $"Bağlantının türü projede yok ({orphan.Id}).")
            : null;
    }

    private Error? ValidateGraphics()
    {
        var pageIds = _pages.Select(page => page.Id).ToHashSet();
        foreach (var graphic in _graphics)
        {
            if (!pageIds.Contains(graphic.PageId))
            {
                return new Error("grafik.sayfa_yok", $"Grafiğin sayfası modelde yok ({graphic.Id}).");
            }

            if (graphic.Shape.Check() is { } problem)
            {
                return new Error("grafik.gecersiz", $"Grafik geçersiz ({graphic.Id}): {problem}.");
            }
        }

        return null;
    }

    private Error? ValidateStructure()
    {
        foreach (var node in _structure)
        {
            if (node.ParentId is not { } parentId)
            {
                continue;
            }

            if (!_nodesById.TryGetValue(parentId, out var parent))
            {
                return new Error("structure.ust_yok", $"'{node.Name}' düğümünün üst düğümü bulunamadı ({parentId}).");
            }

            if (parent.Aspect != node.Aspect)
            {
                return new Error(
                    "structure.aspect_uyumsuz",
                    $"'{node.Name}' düğümü ile üst düğümü '{parent.Name}' farklı aspect'te.");
            }
        }

        foreach (var node in _structure)
        {
            var visited = new HashSet<StructureNodeId>();
            for (var current = node; current is not null; current = ParentOf(current))
            {
                if (!visited.Add(current.Id))
                {
                    return new Error("structure.dongu", $"'{node.Name}' düğümünün üst zinciri kendine dönüyor.");
                }
            }
        }

        return null;
    }

    private StructureNode? ParentOf(StructureNode node) =>
        node.ParentId is { } parentId ? _nodesById.GetValueOrDefault(parentId) : null;

    internal Error? CheckNode(StructureNodeId? id, Aspect expected, string owner)
    {
        if (id is not { } nodeId)
        {
            return null;
        }

        if (!_nodesById.TryGetValue(nodeId, out var node))
        {
            return new Error("structure.dugum_yok", $"{owner}: yapı düğümü bulunamadı ({nodeId}).");
        }

        return node.Aspect == expected
            ? null
            : new Error("structure.aspect_uyumsuz", $"{owner}: '{node.Name}' düğümü {expected} aspect'inde değil.");
    }

    private Error? ValidateDevices()
    {
        foreach (var symbol in _symbols)
        {
            if (symbol.Check() is { } symbolError)
            {
                return symbolError;
            }
        }

        foreach (var device in _devices)
        {
            var owner = $"-{device.ProductTag}";
            var error = CheckNode(device.FunctionNodeId, Aspect.Function, owner)
                ?? CheckNode(device.LocationNodeId, Aspect.Location, owner);
            if (error is not null)
            {
                return error;
            }

            foreach (var function in device.Functions)
            {
                if (CheckSymbolFits(function, owner) is { } symbolError)
                {
                    return symbolError;
                }

                if (function.Role != TerminalRole.Normal && (function.Kind != FunctionKind.Terminal || !Enum.IsDefined(function.Role)))
                {
                    return new Error("function.tur_gecersiz", $"{owner}: klemens türü yalnızca klemens function'ında olabilir ({function.Role}).");
                }

                if (function.Role == TerminalRole.EndCover && function.ConnectionPoints.Count > 0)
                {
                    return new Error("function.sonlandirici_nokta", $"{owner}: sonlandırıcının bağlantı noktası olamaz.");
                }

                if (function.Level is < 1 or > Function.MaxLevel || (function.Level > 1 && function.Kind != FunctionKind.Terminal))
                {
                    return new Error("function.kat_gecersiz", $"{owner}: kat 1–{Function.MaxLevel} olmalı ve yalnızca klemenste 1'den büyük olabilir ({function.Level}).");
                }
            }
        }

        return null;
    }

    /// <summary>Function'ın sembolü varsa projede olmalı ve her bağlantı noktasına bir uç vermeli.</summary>
    internal Error? CheckSymbolFits(Function function, string owner)
    {
        if (function.SymbolId is not { } symbolId)
        {
            return null;
        }

        var symbol = FindSymbol(symbolId);
        if (symbol is null)
        {
            return new Error("sembol.yok", $"{owner}: function'ın sembolü projede yok ({symbolId}).");
        }

        return symbol.Pins.Count == function.ConnectionPoints.Count
            ? null
            : new Error(
                "sembol.uc_uyumsuz",
                $"{owner}: '{symbol.Name}' sembolünün {symbol.Pins.Count} ucu var, function'ın {function.ConnectionPoints.Count} bağlantı noktası var.");
    }

    private Error? ValidateConnections()
    {
        var coreIds = CoreIds();
        var usedCores = new HashSet<CableCoreId>();

        foreach (var connection in _connections)
        {
            if (CheckConnection(connection, coreIds, usedCores) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>
    /// Tek bir bağlantının kurallarını denetler. Damarı varsa <paramref name="usedCores"/> kümesine
    /// ekler; küme, damarın başka bağlantıda kullanılıp kullanılmadığını izler.
    /// </summary>
    internal Error? CheckConnection(Connection connection, HashSet<CableCoreId> coreIds, HashSet<CableCoreId> usedCores)
    {
        if (!_pointsById.ContainsKey(connection.From) || !_pointsById.ContainsKey(connection.To))
        {
            return new Error("connection.uc_yok", $"Bağlantının bir ucu modelde yok ({connection.Id}).");
        }

        if (connection.From == connection.To)
        {
            return new Error(
                "connection.kendine",
                $"Bağlantının iki ucu aynı nokta: {DesignationOf(connection.From)}");
        }

        if (CheckRoute(connection.Route) is { } problem)
        {
            return new Error("connection.yol_gecersiz", $"Bağlantının köşe noktaları geçersiz ({connection.Id}): {problem}.");
        }

        return CheckMedium(connection.Id, connection.Wire, connection.CoreId, coreIds, usedCores);
    }

    /// <summary>Köşe noktası sayısı üst sınırı; daha fazlası elle verilen bir yol için anlamsızdır.</summary>
    public const int MaxRoutePoints = 32;

    /// <summary>Köşe noktalarını denetler; geçerliyse <c>null</c>.</summary>
    internal static string? CheckRoute(IReadOnlyList<WirePoint> route) =>
        route.Count > MaxRoutePoints ? $"en çok {MaxRoutePoints} nokta verilebilir"
        : route.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ? "koordinatlar sonlu sayı olmalı"
        : null;

    internal static Error? CheckMedium(
        ConnectionId connectionId,
        Wire? wire,
        CableCoreId? coreId,
        HashSet<CableCoreId> coreIds,
        HashSet<CableCoreId> usedCores)
    {
        if (wire is not null && coreId is not null)
        {
            return new Error(
                "connection.ortam_cift",
                $"Bağlantıya hem tel hem kablo damarı atanmış ({connectionId}).");
        }

        if (coreId is not { } id)
        {
            return null;
        }

        if (!coreIds.Contains(id))
        {
            return new Error("connection.damar_yok", $"Bağlantının kablo damarı modelde yok ({id}).");
        }

        return usedCores.Add(id)
            ? null
            : new Error("core.cift_atama", $"Kablo damarı birden çok bağlantıya atanmış ({id}).");
    }

    private Error? ValidatePotentialSources()
    {
        foreach (var source in _potentialSources)
        {
            if (!_pointsById.ContainsKey(source.At))
            {
                return new Error("potential.uc_yok", $"'{source.Name}' potansiyel işaretinin noktası modelde yok.");
            }
        }

        return null;
    }

    private Error? ValidatePlc()
    {
        var deviceIds = _devices.Select(device => device.Id).ToHashSet();
        var functionIds = _devices.SelectMany(device => device.Functions).Select(function => function.Id).ToHashSet();

        Error? CheckDevice(DeviceId id, string owner) =>
            deviceIds.Contains(id) ? null : new Error("plc.device_yok", $"{owner}: fiziksel ürün modelde yok ({id}).");

        foreach (var controller in _controllers)
        {
            if (CheckDevice(controller.DeviceId, $"Controller '{controller.Name}'") is { } controllerError)
            {
                return controllerError;
            }

            foreach (var network in controller.Networks)
            {
                var portIds = network.Nodes.SelectMany(node => node.Ports).Select(port => port.Id).ToHashSet();

                foreach (var link in network.PortLinks)
                {
                    if (!portIds.Contains(link.From) || !portIds.Contains(link.To))
                    {
                        return new Error(
                            "plc.port_yok",
                            $"'{network.Name}' ağında port bağlantısının bir ucu bu ağda yok ({link.Id}).");
                    }
                }

                foreach (var node in network.Nodes)
                {
                    if (CheckDevice(node.DeviceId, $"Node '{node.Name}'") is { } nodeError)
                    {
                        return nodeError;
                    }

                    foreach (var module in node.Modules)
                    {
                        var moduleOwner = $"Node '{node.Name}' modül {module.Position}";
                        if (CheckDevice(module.DeviceId, moduleOwner) is { } moduleError)
                        {
                            return moduleError;
                        }

                        foreach (var channel in module.Channels)
                        {
                            if (channel.FunctionId is { } functionId && !functionIds.Contains(functionId))
                            {
                                return new Error(
                                    "plc.function_yok",
                                    $"{moduleOwner} kanal {channel.Index}: function modelde yok ({functionId}).");
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private Error? ValidatePages()
    {
        foreach (var template in _sheetTemplates)
        {
            if (template.Check() is { } templateError)
            {
                return templateError;
            }
        }

        foreach (var page in _pages)
        {
            if (page.TemplateId is { } templateId && _sheetTemplates.All(template => template.Id != templateId))
            {
                return new Error("page.sablon_yok", $"Sayfa {page.Number}: sayfa şablonu modelde yok ({templateId}).");
            }
        }

        foreach (var page in _pages)
        {
            var owner = $"Sayfa {page.Number}";
            var error = CheckNode(page.FunctionNodeId, Aspect.Function, owner)
                ?? CheckNode(page.LocationNodeId, Aspect.Location, owner);
            if (error is not null)
            {
                return error;
            }
        }

        var pageIds = _pages.Select(page => page.Id).ToHashSet();
        var functionIds = _devices.SelectMany(device => device.Functions).Select(function => function.Id).ToHashSet();
        var functionsWithMain = new HashSet<FunctionId>();

        foreach (var placement in _placements)
        {
            if (!pageIds.Contains(placement.PageId))
            {
                return new Error("placement.sayfa_yok", $"Yerleşimin sayfası modelde yok ({placement.Id}).");
            }

            if (!functionIds.Contains(placement.FunctionId))
            {
                return new Error("placement.function_yok", $"Yerleşimin function'ı modelde yok ({placement.Id}).");
            }

            if (placement.RotationDeg is not (0 or 90 or 180 or 270))
            {
                return new Error(
                    "placement.donus_gecersiz",
                    $"Yerleşim dönüşü 0, 90, 180 ya da 270 olmalı; verilen: {placement.RotationDeg}");
            }

            if (placement.Role == PlacementRole.Main && !functionsWithMain.Add(placement.FunctionId))
            {
                return new Error(
                    "placement.ana_tekrar",
                    $"Function'ın birden çok ana görünümü var ({placement.FunctionId}).");
            }
        }

        return null;
    }
}
