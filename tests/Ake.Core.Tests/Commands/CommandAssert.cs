using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Core.Tests.Commands;

/// <summary>Referans makineyi her test için yeniden yükler; testler birbirinin modelini görmez.</summary>
internal static class TestData
{
    public static Project RefMachine()
    {
        var result = ProjectFile.Load(Path.Combine(RepositoryRoot(), "examples", "ref-machine", "ref-machine.json"));
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    public static Device Device(this Project project, string tag) =>
        project.Devices.Single(device => device.ProductTag == tag);

    public static ConnectionPointId Point(this Project project, string tag, string name) =>
        project.Device(tag).Functions.SelectMany(function => function.ConnectionPoints).Single(point => point.Name == name).Id;

    public static Function Function(this Project project, string tag, string pointName) =>
        project.Device(tag).Functions.Single(function => function.ConnectionPoints.Any(point => point.Name == pointName));

    public static Connection Connection(this Project project, string fromTag, string from, string toTag, string to) =>
        project.Connections.Single(
            connection => connection.From == project.Point(fromTag, from) && connection.To == project.Point(toTag, to));

    public static Channel Channel(this Project project, string moduleTag, string pointName) =>
        project.Channels.Single(channel => channel.FunctionId == project.Function(moduleTag, pointName).Id);

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }
}

internal static class CommandAssert
{
    /// <summary>
    /// Command çalışır, modeli değiştirir; undo modeli bayt bayt eski haline, redo yeni haline
    /// getirir. Her adımdan sonra iç tutarlılık korunur. Model command uygulanmış halde bırakılır.
    /// </summary>
    public static void RoundTrips(Project project, ICommand command)
    {
        var stack = new CommandStack(project);
        var before = ProjectJson.Serialize(project);

        var executed = stack.Execute(command, ChangeSource.User, "test");
        Assert.True(executed.IsSuccess, executed.Error?.Message);
        Integrity(project);
        var after = ProjectJson.Serialize(project);
        Assert.NotEqual(before, after);

        Assert.True(stack.Undo().IsSuccess);
        Integrity(project);
        Assert.Equal(before, ProjectJson.Serialize(project));

        Assert.True(stack.Redo().IsSuccess);
        Integrity(project);
        Assert.Equal(after, ProjectJson.Serialize(project));
    }

    /// <summary>Command reddedilir ve model hiç değişmez.</summary>
    public static void Rejected(Project project, ICommand command, string expectedCode)
    {
        var stack = new CommandStack(project);
        var before = ProjectJson.Serialize(project);

        var result = stack.Execute(command, ChangeSource.User, "test");

        Assert.Equal(expectedCode, result.Error?.Code);
        Assert.Equal(before, ProjectJson.Serialize(project));
        Assert.False(stack.CanUndo);
        Assert.Empty(stack.Audit);
    }

    public static void Integrity(Project project)
    {
        var integrity = project.CheckIntegrity();
        Assert.True(integrity.IsSuccess, integrity.Error?.Message);
    }
}
