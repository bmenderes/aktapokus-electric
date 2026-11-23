using Ake.Core.Model;

namespace Ake.Core.Tests.Model;

/// <summary>Testlerde küçük modeller kurmak için kısaltmalar.</summary>
internal static class Build
{
    /// <summary>Tek function'lı device; bağlantı noktaları verilen adlarla oluşur.</summary>
    public static Device Device(string tag, params string[] points) =>
        Device(tag, passThrough: false, points);

    public static Device Device(string tag, bool passThrough, params string[] points) =>
        new(
            DeviceId.New(),
            tag,
            [
                new Function(
                    FunctionId.New(),
                    passThrough ? FunctionKind.Terminal : FunctionKind.Generic,
                    points.Select(name => new ConnectionPoint(ConnectionPointId.New(), name)),
                    passThrough),
            ]);

    public static ConnectionPointId Point(this Device device, string name) =>
        device.Functions.SelectMany(function => function.ConnectionPoints).Single(point => point.Name == name).Id;

    public static Connection Connect(Device from, string fromPoint, Device to, string toPoint) =>
        new(ConnectionId.New(), from.Point(fromPoint), to.Point(toPoint));

    public static PotentialSource Source(string name, Device device, string point) =>
        new(PotentialSourceId.New(), name, PotentialKind.Other, device.Point(point));

    public static Project Project(
        IEnumerable<Device>? devices = null,
        IEnumerable<Connection>? connections = null,
        IEnumerable<PotentialSource>? potentialSources = null)
    {
        var result = Ake.Core.Model.Project.Create(
            ProjectId.New(), "test", devices: devices, connections: connections, potentialSources: potentialSources);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }
}
