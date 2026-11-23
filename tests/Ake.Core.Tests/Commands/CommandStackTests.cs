using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Tests.Model;
using Ake.Serialization;

namespace Ake.Core.Tests.Commands;

public class CommandStackTests
{
    private static AddDevice AddContactor(string tag) => new(Build.Device(tag, "A1", "A2"));

    [Fact]
    public void Undo_and_redo_on_an_empty_stack_fail_without_throwing()
    {
        var stack = new CommandStack(Build.Project());

        Assert.Equal("komut.geri_alinacak_yok", stack.Undo().Error!.Code);
        Assert.Equal("komut.yinelenecek_yok", stack.Redo().Error!.Code);
    }

    [Fact]
    public void Commands_are_undone_in_reverse_order()
    {
        var project = Build.Project();
        var stack = new CommandStack(project);
        stack.Execute(AddContactor("K1"), ChangeSource.User, "test");
        stack.Execute(AddContactor("K2"), ChangeSource.User, "test");

        stack.Undo();

        Assert.Equal(["K1"], project.Devices.Select(device => device.ProductTag));
        Assert.True(stack.CanUndo);
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void A_new_command_discards_the_redo_history()
    {
        var project = Build.Project();
        var stack = new CommandStack(project);
        stack.Execute(AddContactor("K1"), ChangeSource.User, "test");
        stack.Undo();

        stack.Execute(AddContactor("K2"), ChangeSource.User, "test");

        Assert.False(stack.CanRedo);
        Assert.Equal(["K2"], project.Devices.Select(device => device.ProductTag));
    }

    [Fact]
    public void Audit_records_who_did_what_and_when_including_undo_and_redo()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
        var stack = new CommandStack(Build.Project(), clock);

        stack.Execute(AddContactor("K1"), ChangeSource.Ai, "asistan");
        stack.Undo();
        stack.Redo();

        Assert.Equal([AuditAction.Execute, AuditAction.Undo, AuditAction.Redo], stack.Audit.Select(entry => entry.Action));
        Assert.All(stack.Audit, entry =>
        {
            Assert.Equal(ChangeSource.Ai, entry.Source);
            Assert.Equal("asistan", entry.Actor);
            Assert.Equal(clock.GetUtcNow(), entry.At);
            Assert.Equal("Device eklendi: -K1", entry.Summary);
        });
    }

    [Fact]
    public void A_rejected_command_leaves_no_trace()
    {
        var project = Build.Project();

        CommandAssert.Rejected(project, new AddDevice(Build.Device("K 1", "A1")), "tag.gecersiz");
    }

    [Fact]
    public void Transaction_is_a_single_undo_step()
    {
        var project = Build.Project();
        var stack = new CommandStack(project);

        stack.Execute(new Transaction("iki device", [AddContactor("K1"), AddContactor("K2")]), ChangeSource.User, "test");
        Assert.Equal(2, project.Devices.Count);

        stack.Undo();

        Assert.Empty(project.Devices);
        Assert.False(stack.CanUndo);
        Assert.Single(stack.Audit, entry => entry.Action == AuditAction.Execute);
    }

    [Fact]
    public void Transaction_that_fails_midway_changes_nothing()
    {
        var project = Build.Project([Build.Device("K1", "A1")]);
        var before = ProjectJson.Serialize(project);
        var transaction = new Transaction(
            "yarım kalan",
            [AddContactor("K2"), AddContactor("K3"), new RemoveDevice(DeviceId.New())]);

        var result = new CommandStack(project).Execute(transaction, ChangeSource.User, "test");

        Assert.Equal("device.yok", result.Error!.Code);
        Assert.Contains("Adım 3/3", result.Error.Message);
        Assert.Equal(before, ProjectJson.Serialize(project));
    }

    [Fact]
    public void Empty_transaction_succeeds()
    {
        Assert.True(new Transaction("boş", []).Execute(Build.Project()).IsSuccess);
    }

    [Fact]
    public void Deferred_command_sees_what_earlier_steps_added()
    {
        var project = Build.Project([Build.Device("T1", "+")]);
        var added = Build.Device("K1", "A1", "A2");
        var connectToNewDevice = new DeferredCommand(
            "bağla",
            current => Result.Success<ICommand>(new Connect(new Connection(
                ConnectionId.New(),
                current.Devices.Single(device => device.ProductTag == "T1").Point("+"),
                current.Devices.Single(device => device.ProductTag == "K1").Point("A1")))));

        CommandAssert.RoundTrips(project, new Transaction("ekle ve bağla", [new AddDevice(added), connectToNewDevice]));

        Assert.Single(project.Connections);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
