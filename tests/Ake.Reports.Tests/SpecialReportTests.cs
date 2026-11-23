using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Reports.Tests;

public sealed class SpecialReportTests
{
    [Fact]
    public void Four_special_reports_build_tables_for_motors_islands_controller_and_driver_io()
    {
        var project = Project.Create(ProjectId.New(), "Özel Raporlar").Value;

        var motor = new Device(
            DeviceId.New(),
            "-M1",
            [new Function(FunctionId.New(), FunctionKind.Motor, [new ConnectionPoint(ConnectionPointId.New(), "U1"), new ConnectionPoint(ConnectionPointId.New(), "V1"), new ConnectionPoint(ConnectionPointId.New(), "W1")])],
            DeviceKind.Generic,
            description: "Besleme ekseni",
            part: new PartRef("Schneider", "SH100", "SH100/40"),
            address: "101",
            properties: new Dictionary<string, string>
            {
                ["motorType"] = "SH100/40",
                ["gearbox"] = "i=10",
                ["driverType"] = "LXM62",
                ["driverName"] = "D_Besleme",
                ["powerKw"] = "1.5 kW",
            });
        Assert.True(new AddDevice(motor).Execute(project).IsSuccess);

        var valve = new Device(
            DeviceId.New(),
            "-Y1",
            [new Function(FunctionId.New(), FunctionKind.Generic, [new ConnectionPoint(ConnectionPointId.New(), "1"), new ConnectionPoint(ConnectionPointId.New(), "2")])],
            description: "VTUG-1 valf adası",
            properties: new Dictionary<string, string>
            {
                ["island"] = "VTUG-1",
                ["port"] = "1",
                ["signalName"] = "Giriş valfi",
                ["signalLabel"] = "-Y1",
                ["elementType"] = "VALF",
            });
        Assert.True(new AddDevice(valve).Execute(project).IsSuccess);

        var plcCpu = new Device(DeviceId.New(), "-PLC1", [new Function(FunctionId.New(), FunctionKind.PlcChannel, [new ConnectionPoint(ConnectionPointId.New(), "ETH1")])]);
        var lmcMod = new Device(
            DeviceId.New(),
            "-A1",
            [new Function(FunctionId.New(), FunctionKind.PlcChannel, [new ConnectionPoint(ConnectionPointId.New(), "I0")])],
            part: new PartRef("Schneider", "TM5SDI12D", "TM5SDI12D"),
            properties: new Dictionary<string, string> { ["ch.0.terminal"] = "X1:1" });
        var drvNodeDev = new Device(
            DeviceId.New(),
            "D_Besleme",
            [new Function(FunctionId.New(), FunctionKind.PlcChannel, [new ConnectionPoint(ConnectionPointId.New(), "DI0")])],
            properties: new Dictionary<string, string> { ["driverType"] = "LXM62" });
        var drvMod = new Device(
            DeviceId.New(),
            "-A2",
            [new Function(FunctionId.New(), FunctionKind.PlcChannel, [new ConnectionPoint(ConnectionPointId.New(), "DI0")])],
            part: new PartRef("Schneider", "LXM62", "LXM62"),
            properties: new Dictionary<string, string> { ["ch.0.terminal"] = "CN1:5", ["driverIoGroup"] = "LXM62 D_Besleme" });

        Assert.True(new AddDevice(plcCpu).Execute(project).IsSuccess);
        Assert.True(new AddDevice(lmcMod).Execute(project).IsSuccess);
        Assert.True(new AddDevice(drvNodeDev).Execute(project).IsSuccess);
        Assert.True(new AddDevice(drvMod).Execute(project).IsSuccess);

        var lmcNode = new Node(
            NodeId.New(),
            plcCpu.Id,
            "Ana Şasi",
            0,
            [new Module(ModuleId.New(), lmcMod.Id, 1, [new Channel(ChannelId.New(), 0, SignalType.DI, null, "%IX0.0", "Acil Stop", "-S1")])]);
        var drvNode = new Node(
            NodeId.New(),
            drvNodeDev.Id,
            "LXM62 D_Besleme",
            1,
            [new Module(ModuleId.New(), drvMod.Id, 1, [new Channel(ChannelId.New(), 0, SignalType.DI, null, "DI0", "Sürücü Enable", "D_Besleme")])]);
        var ctrl = new Controller(
            ControllerId.New(),
            plcCpu.Id,
            "-PLC1",
            [new Network(NetworkId.New(), NetworkKind.LocalBus, "LMC", [lmcNode, drvNode])]);
        Assert.True(new AddController(ctrl).Execute(project).IsSuccess);

        var motors = ReportCatalog.Build("motors", project);
        Assert.True(motors.IsSuccess, motors.Error?.Message);
        Assert.Contains(motors.Value.Rows, r => r.Contains("D_Besleme") && r.Contains("1.5 kW"));

        var islands = ReportCatalog.Build("islands", project);
        Assert.True(islands.IsSuccess, islands.Error?.Message);
        Assert.Contains(islands.Value.Rows, r => r.Contains("VTUG-1") && r.Contains("Giriş valfi"));

        var ctrlBlocks = ReportCatalog.Build("controller-io-blocks", project);
        Assert.True(ctrlBlocks.IsSuccess, ctrlBlocks.Error?.Message);
        Assert.Contains(ctrlBlocks.Value.Rows, r => r.Contains("X1:1") && r.Contains("Acil Stop"));

        var drvBlocks = ReportCatalog.Build("driver-io-blocks", project);
        Assert.True(drvBlocks.IsSuccess, drvBlocks.Error?.Message);
        Assert.Contains(drvBlocks.Value.Rows, r => r.Contains("CN1:5") && r.Contains("Sürücü Enable"));
    }
}
