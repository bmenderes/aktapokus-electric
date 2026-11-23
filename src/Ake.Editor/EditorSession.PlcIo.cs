using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Library;

namespace Ake.Editor;

/// <summary>PLC adresi ↔ tel numarası çapraz referans tablosunun bir satırı (Faz 6c-4, ADR-0056).</summary>
public sealed record PlcCrossReferenceRow(
    ChannelId ChannelId,
    string Controller,
    string Module,
    string Channel,
    string? Address,
    string? SymbolName,
    string? WireNumber,
    string? Terminal,
    string? FieldTarget,
    string? ModulePoint,
    string? Description,
    FunctionId? FunctionId);

/// <summary>PLC adresi ↔ tel numarası çapraz referansı, kanal atama ve CSV adres listesi içe aktarma, oturum tarafı.</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// Projedeki bütün PLC kanallarını tel numaraları, klemensleri ve saha elemanlarıyla listeler;
    /// PLC adresine veya tel numarasına göre sıralar ve isteğe bağlı süzgeçle filtreler.
    /// </summary>
    public IReadOnlyList<PlcCrossReferenceRow> PlcCrossReferenceRows(bool sortByWire = false, string? filter = null)
    {
        if (Project is not { } project)
        {
            return [];
        }

        var connectivity = Connectivity.Compute(project);
        var cores = project.Cables
            .SelectMany(cable => cable.Cores.Select(core => (cable, core)))
            .ToDictionary(item => item.core.Id, item => item);
        var functions = project.Devices
            .SelectMany(device => device.Functions.Select(function => (device, function)))
            .ToDictionary(item => item.function.Id);

        var items = new List<(string? FirstWire, int Order, PlcCrossReferenceRow Row)>();
        var order = 0;

        foreach (var controller in project.Controllers)
        {
            foreach (var network in controller.Networks)
            {
                foreach (var node in network.Nodes.OrderBy(n => n.Position))
                {
                    foreach (var module in node.Modules.OrderBy(m => m.Position))
                    {
                        var moduleDevice = project.FindDevice(module.DeviceId);
                        var moduleTag = moduleDevice is null ? "—" : project.DesignationOf(moduleDevice).ToString();

                        foreach (var channel in module.Channels.OrderBy(c => c.Index))
                        {
                            string? own = null;
                            var wires = new List<string>();
                            var terminals = new List<string>();
                            var targets = new List<string>();

                            if (channel.FunctionId is { } functionId
                                && functions.TryGetValue(functionId, out var end)
                                && end.function.ConnectionPoints.Count > 0)
                            {
                                var ownPoints = end.function.ConnectionPoints.Select(point => point.Id).ToHashSet();
                                var potential = connectivity.PotentialOf(end.function.ConnectionPoints[0].Id);
                                own = project.DesignationOf(end.function.ConnectionPoints[0].Id)?.ToString();

                                var potPoints = potential.Points.ToHashSet();
                                foreach (var connection in project.Connections.Where(c => potPoints.Contains(c.From) || potPoints.Contains(c.To)))
                                {
                                    if (connection.Wire?.Number is { } number)
                                    {
                                        wires.Add(number);
                                    }
                                    else if (connection.CoreId is { } coreId && cores.TryGetValue(coreId, out var foundCore))
                                    {
                                        wires.Add($"-{foundCore.cable.ProductTag}:{foundCore.core.Designation}");
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
                                        var name = location.Function.Description ?? location.Function.ConnectionPoints.FirstOrDefault()?.Name ?? string.Empty;
                                        terminals.Add($"{project.DesignationOf(location.Device)}:{name}");
                                    }
                                    else
                                    {
                                        targets.Add(project.DesignationOf(point)?.ToString() ?? string.Empty);
                                    }
                                }
                            }

                            var distinctWires = wires.Distinct().ToList();
                            var wireText = distinctWires.Count == 0 ? null : string.Join(", ", distinctWires);
                            var terminalText = terminals.Count == 0 ? null : string.Join(", ", terminals.Distinct());
                            var targetText = targets.Count == 0 ? null : string.Join(", ", targets.Where(t => !string.IsNullOrEmpty(t)));
                            var signal = $"{channel.SignalType}{channel.Index.ToString(CultureInfo.InvariantCulture)}";

                            items.Add((
                                distinctWires.FirstOrDefault(),
                                order++,
                                new PlcCrossReferenceRow(
                                    channel.Id,
                                    controller.Name,
                                    moduleTag,
                                    signal,
                                    channel.Address,
                                    channel.SymbolName,
                                    wireText,
                                    terminalText,
                                    targetText,
                                    own,
                                    channel.Description,
                                    channel.FunctionId)));
                        }
                    }
                }
            }
        }

        var wanted = string.IsNullOrWhiteSpace(filter) ? null : ComponentLibrary.FoldText(filter.Trim());
        bool Matches(PlcCrossReferenceRow row)
        {
            if (wanted is null)
            {
                return true;
            }

            return Contains(row.Address) || Contains(row.SymbolName) || Contains(row.WireNumber)
                || Contains(row.Terminal) || Contains(row.FieldTarget) || Contains(row.Module)
                || Contains(row.Channel) || Contains(row.Description);

            bool Contains(string? value) =>
                !string.IsNullOrEmpty(value) && ComponentLibrary.FoldText(value).Contains(wanted, StringComparison.Ordinal);
        }

        var ordered = sortByWire
            ? items.OrderBy(item => WireOrderKey(item.FirstWire)).ThenBy(item => item.Order)
            : items.OrderBy(item => string.IsNullOrEmpty(item.Row.Address) ? 1 : 0)
                .ThenBy(item => item.Row.Address ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(item => item.Order);

        return [.. ordered.Select(item => item.Row).Where(Matches)];
    }

    /// <summary>PLC kanalının adres, sembolik ad, açıklama ve bağlı function bilgisini günceller.</summary>
    public Result AssignPlcChannel(
        ChannelId channelId, string? address, string? symbolName, string? description, FunctionId? functionId)
    {
        if (Project is null)
        {
            return NoProject();
        }

        var channel = Project.Channels.FirstOrDefault(candidate => candidate.Id == channelId);
        if (channel is null)
        {
            return Result.Failure(new Error("plc.kanal_yok", $"PLC kanalı modelde yok ({channelId})."));
        }

        var addr = CleanOptional(address);
        var sym = CleanOptional(symbolName);
        var desc = CleanOptional(description);
        if (channel.Address == addr && channel.SymbolName == sym && channel.Description == desc && channel.FunctionId == functionId)
        {
            return Result.Success();
        }

        return Execute(new AssignIo(channelId, functionId, sym, addr, desc));
    }

    /// <summary>
    /// Projeye hızlıca örnek/başlangıç PLC denetleyicisi ve G/Ç kanalları ekler (tek adımda geri alınır).
    /// </summary>
    public Result<ControllerId> CreateQuickPlcController(
        string? name = null,
        int diCount = 8,
        int doCount = 8,
        int aiCount = 0,
        int aoCount = 0)
    {
        if (Project is null)
        {
            return Result.Failure<ControllerId>(NoProject().Error!);
        }

        var (device, controller, _) = BuildPlcControllerGraph(
            Project,
            name,
            diCount,
            doCount,
            aiCount,
            aoCount);
        var executed = Execute(new Transaction(
            $"PLC eklendi: {controller.Name}",
            [new AddDevice(device), new AddController(controller)]));
        return executed.IsFailure
            ? Result.Failure<ControllerId>(executed.Error!)
            : Result.Success(controller.Id);
    }

    /// <summary>
    /// CSV / noktalı virgüllü PLC adres listesini (<c>Adres,SembolikAd,Açıklama</c> veya <c>Kanal,Adres,SembolikAd,Açıklama</c>)
    /// kanallara tek geri alma adımında işler; projede PLC kanalı yoksa otomatik oluşturur; güncellenen kanal sayısını döner.
    /// </summary>
    public Result<int> ImportPlcAddressesCsv(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        if (Project is null)
        {
            return Result.Failure<int>(NoProject().Error!);
        }

        var lines = csvText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return Result.Failure<int>(new Error("plc.csv_bos", "İçe aktarılacak PLC adres satırı yok."));
        }

        var delimiter = lines[0].Contains(';', StringComparison.Ordinal) ? ';'
            : lines[0].Contains('\t', StringComparison.Ordinal) ? '\t'
            : ',';

        var commands = new List<ICommand>();
        var allChannels = Project.Channels.ToList();
        if (allChannels.Count == 0)
        {
            var dataLines = lines
                .Select(line => line.Split(delimiter).Select(col => col.Trim().Trim('"')).ToArray())
                .Where((cols, index) => cols.Length > 0 && !(index == 0 && LooksLikeHeader(cols[0])))
                .ToList();
            var doRows = dataLines.Count(cols => cols.Any(c =>
                c.StartsWith("%Q", StringComparison.OrdinalIgnoreCase)
                || c.StartsWith("DO", StringComparison.OrdinalIgnoreCase)));
            var diRows = Math.Max(0, dataLines.Count - doRows);
            var (plcDevice, controller, channels) = BuildPlcControllerGraph(
                Project,
                null,
                Math.Max(8, diRows),
                Math.Max(8, doRows),
                0,
                0);
            commands.Add(new AddDevice(plcDevice));
            commands.Add(new AddController(controller));
            allChannels.AddRange(channels);
        }

        var seqIndex = 0;
        var used = new HashSet<ChannelId>();

        for (var i = 0; i < lines.Length; i++)
        {
            var cols = lines[i].Split(delimiter).Select(col => col.Trim().Trim('"')).ToArray();
            if (cols.Length == 0)
            {
                continue;
            }

            if (i == 0 && LooksLikeHeader(cols[0]))
            {
                continue;
            }

            Channel? target = null;
            string? address;
            string? symbol;
            string? desc;

            if (cols.Length >= 4 || (cols.Length >= 2 && FindChannelBySignalOrAddress(allChannels, cols[0]) is { } explicitChannel && cols.Length == 3 && !cols[0].StartsWith('%')))
            {
                var signalKey = cols[0];
                target = FindChannelBySignalOrAddress(allChannels, signalKey);
                address = cols.Length > 1 ? CleanOptional(cols[1]) : null;
                symbol = cols.Length > 2 ? CleanOptional(cols[2]) : null;
                desc = cols.Length > 3 ? CleanOptional(cols[3]) : null;
            }
            else
            {
                address = CleanOptional(cols[0]);
                symbol = cols.Length > 1 ? CleanOptional(cols[1]) : null;
                desc = cols.Length > 2 ? CleanOptional(cols[2]) : null;
                target = allChannels.FirstOrDefault(ch => !string.IsNullOrEmpty(address) && string.Equals(ch.Address, address, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    while (seqIndex < allChannels.Count && used.Contains(allChannels[seqIndex].Id))
                    {
                        seqIndex++;
                    }

                    if (seqIndex < allChannels.Count)
                    {
                        target = allChannels[seqIndex++];
                    }
                }
            }

            if (target is null)
            {
                continue;
            }

            used.Add(target.Id);

            var finalAddress = address ?? target.Address;
            var finalSymbol = symbol ?? target.SymbolName;
            var finalDesc = desc ?? target.Description;
            if (finalAddress == target.Address && finalSymbol == target.SymbolName && finalDesc == target.Description)
            {
                continue;
            }

            commands.Add(new AssignIo(target.Id, target.FunctionId, finalSymbol, finalAddress, finalDesc));
        }

        if (commands.Count == 0)
        {
            return Result.Success(0);
        }

        var executed = Execute(new Transaction("PLC adres listesi içe aktarıldı", commands));
        return executed.IsFailure ? Result.Failure<int>(executed.Error!) : Result.Success(commands.OfType<AssignIo>().Count());
    }

    private static (Device Device, Controller Controller, List<Channel> Channels) BuildPlcControllerGraph(
        Project project,
        string? name,
        int diCount,
        int doCount,
        int aiCount,
        int aoCount)
    {
        var n = 1;
        while (project.Devices.Any(d => d.ProductTag == $"A{n}")
            || project.Controllers.Any(c => string.Equals(c.Name, $"PLC{n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        var plcTag = $"A{n}";
        var controllerName = string.IsNullOrWhiteSpace(name) ? $"PLC{n}" : name.Trim();
        var device = new Device(DeviceId.New(), plcTag, [], DeviceKind.Generic, description: controllerName);

        var channels = new List<Channel>();
        for (var i = 0; i < diCount; i++)
        {
            channels.Add(new Channel(ChannelId.New(), i, SignalType.DI, address: $"%I{i / 8}.{i % 8}"));
        }

        for (var i = 0; i < doCount; i++)
        {
            channels.Add(new Channel(ChannelId.New(), i, SignalType.DO, address: $"%Q{i / 8}.{i % 8}"));
        }

        for (var i = 0; i < aiCount; i++)
        {
            channels.Add(new Channel(ChannelId.New(), i, SignalType.AI, address: $"%IW{i * 2}"));
        }

        for (var i = 0; i < aoCount; i++)
        {
            channels.Add(new Channel(ChannelId.New(), i, SignalType.AO, address: $"%QW{i * 2}"));
        }

        var module = new Module(ModuleId.New(), device.Id, 1, channels);
        var node = new Node(NodeId.New(), device.Id, controllerName, 1, [module]);
        var network = new Network(NetworkId.New(), NetworkKind.LocalBus, "LocalBus", [node]);
        var controller = new Controller(ControllerId.New(), device.Id, controllerName, [network]);
        return (device, controller, channels);
    }

    private static Channel? FindChannelBySignalOrAddress(IReadOnlyList<Channel> channels, string key) =>
        channels.FirstOrDefault(ch =>
            string.Equals($"{ch.SignalType}{ch.Index.ToString(CultureInfo.InvariantCulture)}", key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ch.Address, key, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeHeader(string firstCell)
    {
        var folded = ComponentLibrary.FoldText(firstCell);
        return folded is "adres" or "address" or "kanal" or "channel" or "sinyal" or "signal" or "nr";
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (int Group, long Number, string Text) WireOrderKey(string? number) =>
        number is null ? (2, 0, string.Empty)
        : long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? (0, value, string.Empty)
        : (1, 0, number);
}
