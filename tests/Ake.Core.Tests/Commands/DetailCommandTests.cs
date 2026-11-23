using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

/// <summary>Cihaz ayrıntıları, function açıklaması ve bağlantı noktası adı (özellik penceresi komutları).</summary>
public class DetailCommandTests
{
    [Fact]
    public void Device_details_round_trip_and_an_empty_part_removes_the_assignment()
    {
        var project = TestData.RefMachine();
        var k1 = project.Device("K1");

        CommandAssert.RoundTrips(
            project, new SetDeviceDetails(k1.Id, "  Ana kontaktör  ", new PartRef("Schneider", "LC1D09BD", "TeSys D")));

        Assert.Equal("Ana kontaktör", k1.Description);
        Assert.Equal(new PartRef("Schneider", "LC1D09BD", "TeSys D"), k1.Part);

        CommandAssert.RoundTrips(project, new SetDeviceDetails(k1.Id, " ", null));
        Assert.Null(k1.Description);
        Assert.Null(k1.Part);
    }

    [Fact]
    public void Device_details_need_manufacturer_and_order_code_together()
    {
        var project = TestData.RefMachine();
        var k1 = project.Device("K1");

        CommandAssert.Rejected(project, new SetDeviceDetails(k1.Id, "x", new PartRef("Schneider", " ")), "device.urun_eksik");
        CommandAssert.Rejected(project, new SetDeviceDetails(k1.Id, "x", new PartRef("", "LC1D09")), "device.urun_eksik");
        CommandAssert.Rejected(project, new SetDeviceDetails(DeviceId.New(), "x", null), "device.yok");
    }

    [Fact]
    public void Function_description_round_trips_and_unknown_functions_are_refused()
    {
        var project = TestData.RefMachine();
        var function = project.Function("K1", "A1");

        CommandAssert.RoundTrips(project, new SetFunctionDescription(function.Id, "Bobin 24 V DC"));

        Assert.Equal("Bobin 24 V DC", function.Description);
        CommandAssert.Rejected(project, new SetFunctionDescription(FunctionId.New(), "x"), "function.yok");
    }

    [Fact]
    public void Renaming_a_point_keeps_its_connections_and_is_undoable()
    {
        var project = TestData.RefMachine();
        var point = project.Point("K1", "A1");
        var connections = project.Connections.Count(c => c.From == point || c.To == point);

        CommandAssert.RoundTrips(project, new RenameConnectionPoint(point, "B1"));

        Assert.Equal("B1", project.Locate(point)!.Point.Name);
        Assert.Equal(connections, project.Connections.Count(c => c.From == point || c.To == point));
        Assert.True(connections > 0);
    }

    [Fact]
    public void Point_names_must_be_valid_and_unique_inside_their_function()
    {
        var project = TestData.RefMachine();
        var a1 = project.Point("K1", "A1");
        var function = project.Function("K1", "A1");
        var other = function.ConnectionPoints.First(point => point.Id != a1).Name;

        CommandAssert.Rejected(project, new RenameConnectionPoint(a1, "  "), "point.ad_gecersiz");
        CommandAssert.Rejected(project, new RenameConnectionPoint(a1, "A 1"), "point.ad_gecersiz");
        CommandAssert.Rejected(project, new RenameConnectionPoint(a1, "K1:A1"), "point.ad_gecersiz");
        CommandAssert.Rejected(project, new RenameConnectionPoint(a1, other), "point.ad_tekrar");
        CommandAssert.Rejected(project, new RenameConnectionPoint(ConnectionPointId.New(), "X"), "point.yok");
    }

    [Fact]
    public void SetDeviceAddress_sets_trims_clears_and_undoes()
    {
        var project = TestData.RefMachine();
        var device = project.Device("K1");

        CommandAssert.RoundTrips(project, new SetDeviceAddress(device.Id, " 192.168.0.50 "));
        Assert.Equal("192.168.0.50", device.Address);

        CommandAssert.RoundTrips(project, new SetDeviceAddress(device.Id, "  "));
        Assert.Null(device.Address);
    }

    [Fact]
    public void SetDeviceAddress_rejects_a_missing_device_spaces_inside_and_a_too_long_address()
    {
        var project = TestData.RefMachine();
        var device = project.Device("K1");

        CommandAssert.Rejected(project, new SetDeviceAddress(DeviceId.New(), "1.2.3.4"), "device.yok");
        CommandAssert.Rejected(project, new SetDeviceAddress(device.Id, "192.168 0.1"), "device.adres_gecersiz");
        CommandAssert.Rejected(project, new SetDeviceAddress(device.Id, new string('1', 65)), "device.adres_gecersiz");
    }
}
