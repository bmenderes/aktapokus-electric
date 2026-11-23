using System.Globalization;
using System.Text;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using ClosedXML.Excel;

namespace Ake.Library;

/// <summary>SEE Electrical dışa aktarım listelerinden (Excel/CSV) modele alınan öğelerin özeti (ADR-0057).</summary>
public sealed record SeeListImportSummary(
    int DevicesAdded,
    int DevicesUpdated,
    int ModulesAdded,
    int ChannelsAdded,
    int TerminalsAdded,
    int NodesAdded);

/// <summary>
/// SEE Electrical'dan dışa aktarılan Excel (.xlsx) ve CSV mühendislik listelerini
/// (Motor/Redüktör, Ada listesi, Kontrolör I/O ve Sürücü I/O blokları veya düz I/O/cihaz tabloları)
/// tek geri alınabilir <see cref="Transaction"/> olarak projeye aktarır (ADR-0057).
/// </summary>
public static class SeeListImport
{
    public static Result<(Transaction Transaction, SeeListImportSummary Summary)> FromFile(Project project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return Result.Failure<(Transaction, SeeListImportSummary)>(
                new Error("liste.dosya_okunamadi", $"Dosya bulunamadı: '{path}'"));
        }

        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".xlsx")
            {
                using var workbook = new XLWorkbook(path);
                return ParseWorkbook(project, workbook);
            }

            if (ext == ".csv")
            {
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                return ParseCsvLines(project, lines);
            }

            return Result.Failure<(Transaction, SeeListImportSummary)>(
                new Error("liste.bicim_yok", $"Desteklenmeyen liste biçimi '{ext}'. Biçimler: .xlsx, .csv"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            return Result.Failure<(Transaction, SeeListImportSummary)>(
                new Error("liste.okunamadi", $"'{Path.GetFileName(path)}' okunamadı: {ex.Message}"));
        }
    }

    private static Result<(Transaction Transaction, SeeListImportSummary Summary)> ParseWorkbook(Project project, XLWorkbook workbook)
    {
        var state = new ImportBuilder(project);

        foreach (var sheet in workbook.Worksheets)
        {
            var name = Fold(sheet.Name);
            if (name.Contains("motor", StringComparison.Ordinal))
            {
                ImportMotorSheet(sheet, state);
            }
            else if (name.Contains("ada", StringComparison.Ordinal))
            {
                ImportIslandSheet(sheet, state);
            }
            else if (name.Contains("lmc", StringComparison.Ordinal) || name.Contains("plc", StringComparison.Ordinal) || name.Contains("kontrolor", StringComparison.Ordinal))
            {
                ImportIoBlockSheet(sheet, state, isDriver: false);
            }
            else if (name.Contains("driver", StringComparison.Ordinal) || name.Contains("surucu", StringComparison.Ordinal))
            {
                ImportIoBlockSheet(sheet, state, isDriver: true);
            }
            else
            {
                // Başlıktan otomatik tespit
                var headerText = Fold(string.Join(" ", sheet.RowsUsed().Take(5).SelectMany(r => r.CellsUsed().Select(c => c.GetString()))));
                if (headerText.Contains("surucu ismi", StringComparison.Ordinal) || headerText.Contains("motorlar", StringComparison.Ordinal))
                {
                    ImportMotorSheet(sheet, state);
                }
                else if (headerText.Contains("signal name", StringComparison.Ordinal))
                {
                    ImportIslandSheet(sheet, state);
                }
                else if (headerText.Contains("klemens", StringComparison.Ordinal) && headerText.Contains("isim", StringComparison.Ordinal))
                {
                    ImportIoBlockSheet(sheet, state, isDriver: headerText.Contains("lxm", StringComparison.Ordinal));
                }
            }
        }

        if (state.Commands.Count == 0)
        {
            return Result.Failure<(Transaction, SeeListImportSummary)>(
                new Error("liste.bos", "Çalışma kitabında aktarılabilir motor, ada veya I/O verisi bulunamadı."));
        }

        return Result.Success((state.BuildTransaction("SEE listesi içe aktarıldı"), state.Summary));
    }

    private static Result<(Transaction Transaction, SeeListImportSummary Summary)> ParseCsvLines(Project project, string[] lines)
    {
        if (lines.Length < 2)
        {
            return Result.Failure<(Transaction, SeeListImportSummary)>(new Error("liste.bos", "CSV dosyasında veri satırı yok."));
        }

        var separator = lines[0].Contains(';') ? ';' : ',';
        var rows = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => SplitCsvLine(l, separator))
            .ToList();

        using var wb = new XLWorkbook();
        var sheet = wb.Worksheets.Add("Liste");
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = rows[r][c];
            }
        }

        return ParseWorkbook(project, wb);
    }

    private static void ImportMotorSheet(IXLWorksheet sheet, ImportBuilder state)
    {
        var headerRow = FindRowContaining(sheet, "Sürücü İsmi") ?? FindRowContaining(sheet, "Surucu Ismi");
        if (headerRow is null)
        {
            return;
        }

        var headers = ReadHeaderMap(headerRow);
        var colDriverName = FindCol(headers, "surucu ismi");
        var colDriverType = FindCol(headers, "surucu tipi");
        var colSercos = FindCol(headers, "sercos");
        var colCanOpen = FindCol(headers, "can open");
        var colAxis = FindCol(headers, "isim");
        var colMotor = FindCol(headers, "kullanilan motorlar", "motor");
        var colGear = FindCol(headers, "reduktor");
        var colKw = FindCol(headers, "kw");
        var colLength = FindCol(headers, "metraj");

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        for (var r = headerRow.RowNumber() + 1; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var driverName = Cell(row, colDriverName);
            var driverType = Cell(row, colDriverType);
            var sercos = Cell(row, colSercos);
            var canOpen = Cell(row, colCanOpen);
            var axis = Cell(row, colAxis);
            var motor = Cell(row, colMotor);
            var gear = Cell(row, colGear);
            var kw = Cell(row, colKw);
            var length = Cell(row, colLength);

            if (string.IsNullOrWhiteSpace(driverName) && string.IsNullOrWhiteSpace(motor) && string.IsNullOrWhiteSpace(axis))
            {
                continue;
            }

            var tag = SanitizeTag(driverName ?? axis ?? $"M{r}");
            var props = new Dictionary<string, string>(StringComparer.Ordinal);
            AddIfPresent(props, "driverName", driverName);
            AddIfPresent(props, "driverType", driverType);
            AddIfPresent(props, "sercosAddress", sercos);
            AddIfPresent(props, "canOpenAddress", canOpen);
            AddIfPresent(props, "axisName", axis);
            AddIfPresent(props, "motorType", motor);
            AddIfPresent(props, "gearbox", gear);
            AddIfPresent(props, "powerKw", kw);
            AddIfPresent(props, "cableLength", length);

            var part = !string.IsNullOrWhiteSpace(driverType) || !string.IsNullOrWhiteSpace(motor)
                ? new PartRef("Schneider Electric", motor ?? driverType!, driverType ?? motor)
                : null;
            var fnKind = !string.IsNullOrWhiteSpace(motor) ? FunctionKind.Motor : FunctionKind.PowerSupply;
            var fnDesc = axis ?? motor ?? driverType;
            var fn = new Function(
                FunctionId.New(),
                fnKind,
                fnKind == FunctionKind.Motor
                    ? [new ConnectionPoint(ConnectionPointId.New(), "U1"), new ConnectionPoint(ConnectionPointId.New(), "V1"), new ConnectionPoint(ConnectionPointId.New(), "W1")]
                    : [new ConnectionPoint(ConnectionPointId.New(), "L1"), new ConnectionPoint(ConnectionPointId.New(), "L2"), new ConnectionPoint(ConnectionPointId.New(), "L3")],
                description: fnDesc);

            state.UpsertDevice(
                tag,
                axis ?? driverType ?? motor,
                part,
                address: CleanAddress(sercos ?? canOpen),
                functions: [fn],
                properties: props);
        }
    }

    private static void ImportIslandSheet(IXLWorksheet sheet, ImportBuilder state)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow < 4 || lastCol < 3)
        {
            return;
        }

        // Ada bloklarını bul: "Port" ve "Signal Name" sütun başlıklarını ara
        var headerRows = sheet.RowsUsed()
            .Where(r => r.CellsUsed().Any(c => Fold(c.GetString()) == "signal name"))
            .ToList();

        foreach (var hRow in headerRows)
        {
            var rNum = hRow.RowNumber();
            foreach (var cell in hRow.CellsUsed())
            {
                if (Fold(cell.GetString()) != "signal name")
                {
                    continue;
                }

                var nameCol = cell.Address.ColumnNumber;
                var portCol = nameCol - 1;
                var labelCol = nameCol + 1;
                var typeCol = nameCol + 2;
                var rawCol = portCol - 1;

                var titleRow = rNum > 1 ? sheet.Row(rNum - 1) : null;
                var islandCode = titleRow is null ? null : Cell(titleRow, portCol);
                var islandLoc = titleRow is null ? null : Cell(titleRow, nameCol);
                var moduleType = titleRow is null ? null : Cell(titleRow, labelCol);

                for (var r = rNum + 1; r <= lastRow; r++)
                {
                    var dataRow = sheet.Row(r);
                    var signalName = Cell(dataRow, nameCol);
                    var port = Cell(dataRow, portCol);
                    var signalLabel = Cell(dataRow, labelCol);
                    var elementType = Cell(dataRow, typeCol);
                    var rawPort = rawCol >= 1 ? Cell(dataRow, rawCol) : null;
                    if (string.IsNullOrWhiteSpace(signalName) && string.IsNullOrWhiteSpace(signalLabel))
                    {
                        break;
                    }

                    var candidateTag = ExtractTagFromLabel(signalLabel) ?? SanitizeTag($"{islandCode ?? "ADA"}_{rawPort ?? port ?? r.ToString(CultureInfo.InvariantCulture)}");
                    var props = new Dictionary<string, string>(StringComparer.Ordinal);
                    AddIfPresent(props, "island", islandCode);
                    AddIfPresent(props, "islandLocation", islandLoc);
                    AddIfPresent(props, "moduleType", moduleType);
                    AddIfPresent(props, "port", port ?? rawPort);
                    AddIfPresent(props, "signalName", signalName);
                    AddIfPresent(props, "signalLabel", signalLabel);
                    AddIfPresent(props, "elementType", elementType);

                    var locId = !string.IsNullOrWhiteSpace(islandLoc) ? (StructureNodeId?)state.EnsureLocationNode(SanitizeNodeName(islandLoc)) : null;
                    var part = !string.IsNullOrWhiteSpace(elementType) ? new PartRef("Saha", elementType!, elementType) : null;
                    var fn = new Function(FunctionId.New(), FunctionKind.Generic, [new ConnectionPoint(ConnectionPointId.New(), "1"), new ConnectionPoint(ConnectionPointId.New(), "4")], description: signalName);
                    state.UpsertDevice(candidateTag, signalName, part, locationNodeId: locId, functions: [fn], properties: props);
                }
            }
        }
    }

    private static void ImportIoBlockSheet(IXLWorksheet sheet, ImportBuilder state, bool isDriver)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow < 2 || lastCol < 2)
        {
            return;
        }

        // Blok başlık satırını bul: "Nr" ve "İsim" içeren satır
        var headerRow = sheet.RowsUsed()
            .FirstOrDefault(r => r.CellsUsed().Any(c => Fold(c.GetString()) == "nr")
                && r.CellsUsed().Any(c => Fold(c.GetString()) == "isim"));
        if (headerRow is null)
        {
            return;
        }

        var hNum = headerRow.RowNumber();
        var nrCells = headerRow.CellsUsed().Where(c => Fold(c.GetString()) == "nr").ToList();
        var blocks = new List<(string ModuleTitle, List<(string? Terminal, string Nr, string? Symbol, string? Code)> Channels)>();

        foreach (var nrCell in nrCells)
        {
            var nrCol = nrCell.Address.ColumnNumber;
            // Kontrolör bloklarında solunda "Klemens" vardır: Klemens | Nr | İsim | Kod
            // Sürücü bloklarında "Nr" baştadır: Nr | İsim | Kod | Klemens
            var hasLeftTerminal = nrCol > 1 && Fold(headerRow.Cell(nrCol - 1).GetString()).Contains("klemens", StringComparison.Ordinal);
            var termCol = hasLeftTerminal ? nrCol - 1 : (nrCol + 3 <= lastCol ? nrCol + 3 : -1);
            var nameCol = nrCol + 1;
            var codeCol = nrCol + 2;

            var startCol = hasLeftTerminal ? nrCol - 1 : nrCol;
            var moduleTitle = hNum > 1
                ? string.Join(" ", Enumerable.Range(startCol, 4).Select(c => c <= lastCol ? Cell(sheet.Row(hNum - 1), c) : null).Where(s => !string.IsNullOrWhiteSpace(s)))
                : string.Empty;
            if (string.IsNullOrWhiteSpace(moduleTitle))
            {
                moduleTitle = isDriver ? $"LXM_IO_{blocks.Count + 1}" : $"PLC_IO_{blocks.Count + 1}";
            }

            var channels = new List<(string? Terminal, string Nr, string? Symbol, string? Code)>();
            for (var r = hNum + 1; r <= lastRow; r++)
            {
                var row = sheet.Row(r);
                var nr = Cell(row, nrCol);
                var sym = nameCol <= lastCol ? Cell(row, nameCol) : null;
                var code = codeCol <= lastCol ? Cell(row, codeCol) : null;
                var term = termCol >= 1 ? Cell(row, termCol) : null;
                if (string.IsNullOrWhiteSpace(nr) && string.IsNullOrWhiteSpace(sym) && string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                channels.Add((term, nr ?? $"CH{channels.Count + 1}", sym, code));
            }

            if (channels.Count > 0)
            {
                blocks.Add((moduleTitle, channels));
            }
        }

        if (blocks.Count == 0)
        {
            return;
        }

        var cpuTag = isDriver ? "DRV_CTRL" : "LMC";
        var cpuDevice = state.EnsureControllerDevice(cpuTag, isDriver ? "Sürücü I/O" : "Kontrolör LMC");
        var networkKind = isDriver ? NetworkKind.Sercos : NetworkKind.LocalBus;
        var networkName = isDriver ? "Sercos" : "Yerel Bus";
        var modules = new List<Module>();

        for (var b = 0; b < blocks.Count; b++)
        {
            var block = blocks[b];
            var baseTag = isDriver ? $"DRV{b + 1}" : $"A{b + 1}";
            var modTag = state.UniqueTag(baseTag);
            var modProps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (isDriver)
            {
                modProps["driverIoGroup"] = block.ModuleTitle;
            }

            var modFunctions = new List<Function>();
            var channels = new List<Channel>();
            for (var c = 0; c < block.Channels.Count; c++)
            {
                var ch = block.Channels[c];
                if (!string.IsNullOrWhiteSpace(ch.Terminal))
                {
                    state.EnsureTerminal(ch.Terminal!);
                    modProps[$"ch.{c}.terminal"] = ch.Terminal!;
                    modProps[$"io.{c}.terminal"] = ch.Terminal!;
                }

                if (!string.IsNullOrWhiteSpace(ch.Code))
                {
                    modProps[$"io.{c}.code"] = ch.Code!;
                }

                var fn = new Function(FunctionId.New(), FunctionKind.PlcChannel, [new ConnectionPoint(ConnectionPointId.New(), ch.Nr)], description: ch.Symbol ?? ch.Code);
                modFunctions.Add(fn);
                var sigType = InferSignalType(ch.Nr);
                channels.Add(new Channel(ChannelId.New(), c, sigType, fn.Id, ch.Nr, ch.Symbol, ch.Code));
                state.ChannelsAdded++;
            }

            var modDev = state.UpsertDevice(
                modTag,
                block.ModuleTitle,
                new PartRef("Schneider Electric", SanitizeTag(block.ModuleTitle), block.ModuleTitle),
                functions: modFunctions,
                properties: modProps);
            modules.Add(new Module(ModuleId.New(), modDev.Id, b + 1, channels));
            state.ModulesAdded++;
        }

        var node = new Node(NodeId.New(), cpuDevice.Id, isDriver ? "Sürücüler" : "Ana Şasi", 0, modules);
        var controller = new Controller(ControllerId.New(), cpuDevice.Id, cpuTag, [new Network(NetworkId.New(), networkKind, networkName, [node])]);
        state.AddController(controller);
    }

    private sealed class ImportBuilder
    {
        private readonly Project _project;
        private readonly Dictionary<string, Device> _devicesByTag = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StructureNodeId> _locations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Device> _strips = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _stripTerminals = new(StringComparer.OrdinalIgnoreCase);

        public List<ICommand> Commands { get; } = [];
        public int DevicesAdded { get; private set; }
        public int DevicesUpdated { get; private set; }
        public int ModulesAdded { get; set; }
        public int ChannelsAdded { get; set; }
        public int TerminalsAdded { get; private set; }
        public int NodesAdded { get; private set; }

        public ImportBuilder(Project project)
        {
            _project = project;
            foreach (var device in project.Devices)
            {
                _devicesByTag.TryAdd(device.ProductTag, device);
                if (device.Kind == DeviceKind.TerminalStrip)
                {
                    _strips.TryAdd(device.ProductTag, device);
                    _stripTerminals[device.ProductTag] =
                    [
                        .. device.Functions.Select(f => f.Description ?? f.ConnectionPoints.FirstOrDefault()?.Name ?? string.Empty),
                    ];
                }
            }

            foreach (var node in project.Structure.Where(n => n.Aspect == Aspect.Location))
            {
                _locations.TryAdd(node.Name, node.Id);
            }
        }

        public SeeListImportSummary Summary => new(DevicesAdded, DevicesUpdated, ModulesAdded, ChannelsAdded, TerminalsAdded, NodesAdded);

        public Transaction BuildTransaction(string title) => new(title, Commands);

        public StructureNodeId EnsureLocationNode(string name)
        {
            if (_locations.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var id = StructureNodeId.New();
            Commands.Add(new AddStructureNode(new StructureNode(id, Aspect.Location, name)));
            _locations[name] = id;
            NodesAdded++;
            return id;
        }

        public Device EnsureControllerDevice(string tag, string description)
        {
            if (_devicesByTag.TryGetValue(tag, out var existing))
            {
                return existing;
            }

            var dev = new Device(DeviceId.New(), tag, [], description: description);
            Commands.Add(new AddDevice(dev));
            _devicesByTag[tag] = dev;
            DevicesAdded++;
            return dev;
        }

        public Device UpsertDevice(
            string tag,
            string? description,
            PartRef? part,
            StructureNodeId? locationNodeId = null,
            string? address = null,
            IReadOnlyList<Function>? functions = null,
            IReadOnlyDictionary<string, string>? properties = null)
        {
            if (_devicesByTag.TryGetValue(tag, out var existing))
            {
                if (!string.IsNullOrWhiteSpace(description) || part is not null)
                {
                    Commands.Add(new SetDeviceDetails(existing.Id, description ?? existing.Description, part ?? existing.Part));
                }

                if (properties is not null)
                {
                    foreach (var (k, v) in properties)
                    {
                        Commands.Add(new SetDeviceProperty(existing.Id, k, v));
                    }
                }

                DevicesUpdated++;
                return existing;
            }

            var device = new Device(
                DeviceId.New(),
                tag,
                functions ?? [],
                DeviceKind.Generic,
                null,
                locationNodeId,
                description,
                part,
                null,
                address,
                properties);
            Commands.Add(new AddDevice(device));
            _devicesByTag[tag] = device;
            DevicesAdded++;
            return device;
        }

        public string UniqueTag(string baseTag)
        {
            var clean = SanitizeTag(baseTag);
            if (!_devicesByTag.ContainsKey(clean))
            {
                return clean;
            }

            var i = 2;
            while (_devicesByTag.ContainsKey($"{clean}_{i.ToString(CultureInfo.InvariantCulture)}"))
            {
                i++;
            }

            return $"{clean}_{i.ToString(CultureInfo.InvariantCulture)}";
        }

        public void EnsureTerminal(string raw)
        {
            var trimmed = raw.Trim();
            var sep = trimmed.LastIndexOf(':');
            if (sep < 0)
            {
                sep = trimmed.LastIndexOf('-');
            }

            var rawStrip = sep > 0 ? trimmed[..sep] : "X_IO";
            var rawPin = sep > 0 && sep + 1 < trimmed.Length ? trimmed[(sep + 1)..] : trimmed;
            var stripTag = SanitizeTag(rawStrip);
            var pinName = new string(rawPin.Trim().Select(c => c == ':' || char.IsWhiteSpace(c) ? '_' : c).ToArray());
            if (pinName.Length == 0)
            {
                pinName = "1";
            }

            if (!_strips.TryGetValue(stripTag, out var strip))
            {
                var terminalFn = AddTerminal.Create(pinName);
                strip = new Device(DeviceId.New(), stripTag, [terminalFn], DeviceKind.TerminalStrip);
                Commands.Add(new AddDevice(strip));
                _devicesByTag[stripTag] = strip;
                _strips[stripTag] = strip;
                _stripTerminals[stripTag] = [pinName];
                TerminalsAdded++;
                return;
            }

            if (_stripTerminals[stripTag].Add(pinName))
            {
                var insertIndex = _stripTerminals[stripTag].Count - 1;
                Commands.Add(new AddTerminal(strip.Id, insertIndex, AddTerminal.Create(pinName)));
                TerminalsAdded++;
            }
        }

        public void AddController(Controller controller) => Commands.Add(new AddController(controller));
    }

    private static SignalType InferSignalType(string nr)
    {
        var upper = nr.ToUpperInvariant();
        if (upper.Contains("DQ", StringComparison.Ordinal) || upper.Contains("DO", StringComparison.Ordinal) || upper.StartsWith('Q') || upper.StartsWith('O'))
        {
            return SignalType.DO;
        }

        if (upper.Contains("AI", StringComparison.Ordinal))
        {
            return SignalType.AI;
        }

        if (upper.Contains("AO", StringComparison.Ordinal))
        {
            return SignalType.AO;
        }

        return SignalType.DI;
    }

    private static IXLRow? FindRowContaining(IXLWorksheet sheet, string text)
    {
        var target = Fold(text);
        return sheet.RowsUsed().FirstOrDefault(r => r.CellsUsed().Any(c => Fold(c.GetString()).Contains(target, StringComparison.Ordinal)));
    }

    private static Dictionary<string, int> ReadHeaderMap(IXLRow row)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in row.CellsUsed())
        {
            var key = Fold(cell.GetString());
            if (key.Length > 0 && !map.ContainsKey(key))
            {
                map[key] = cell.Address.ColumnNumber;
            }
        }

        return map;
    }

    private static int FindCol(Dictionary<string, int> headers, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (headers.TryGetValue(candidate, out var exact))
            {
                return exact;
            }

            foreach (var (key, col) in headers)
            {
                if (key.Contains(candidate, StringComparison.Ordinal))
                {
                    return col;
                }
            }
        }

        return -1;
    }

    private static string? Cell(IXLRow row, int col)
    {
        if (col < 1)
        {
            return null;
        }

        var text = row.Cell(col).GetString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? CleanAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return cleaned.Length is > 0 and <= 64 ? cleaned : null;
    }

    private static string? ExtractTagFromLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var dash = label.IndexOf('-');
        if (dash < 0 || dash + 1 >= label.Length)
        {
            return null;
        }

        var afterDash = label[(dash + 1)..];
        var slash = afterDash.IndexOf('/');
        var candidate = slash > 0 ? afterDash[..slash] : afterDash;
        return SanitizeTag(candidate);
    }

    private static string SanitizeTag(string raw)
    {
        var chars = raw.Trim().Select(c => c is '=' or '+' or ':' || char.IsWhiteSpace(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim('-');
        return result.Length == 0 ? "D1" : result;
    }

    private static string SanitizeNodeName(string raw)
    {
        var chars = raw.Trim().Select(c => c is '=' or '+' or ':' or '.' || char.IsWhiteSpace(c) ? '_' : c).ToArray();
        var result = new string(chars);
        return result.Length == 0 ? "L1" : result;
    }

    private static void AddIfPresent(Dictionary<string, string> props, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            props[key] = value.Trim();
        }
    }

    private static string Fold(string text) =>
        text.Trim()
            .Replace('İ', 'i')
            .Replace('I', 'i')
            .ToLowerInvariant()
            .Replace("\u0307", string.Empty)
            .Replace('ı', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');

    private static List<string> SplitCsvLine(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (ch == separator && !quoted)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }
}
