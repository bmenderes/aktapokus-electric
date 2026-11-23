using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

/// <summary>Bağlantı türleri: renk ve çizgi biçimi (ADR-0036).</summary>
public class ConnectionKindTests
{
    private static ConnectionKind Profinet() => new(ConnectionKindId.New(), "Profinet", "#2E9E2E", LineStyle.Solid, PinKind.Communication);

    [Fact]
    public void A_kind_is_added_replaced_in_place_and_undone()
    {
        var project = TestData.RefMachine();
        var kind = Profinet();

        CommandAssert.RoundTrips(project, new PutConnectionKind(kind));
        CommandAssert.RoundTrips(project, new PutConnectionKind(kind with { Color = "#00AA00", Style = LineStyle.Dashed }));

        var stored = Assert.Single(project.ConnectionKinds);
        Assert.Equal(("#00AA00", LineStyle.Dashed), (stored.Color, stored.Style));
    }

    [Fact]
    public void Invalid_kinds_are_rejected_and_names_are_unique_ignoring_case()
    {
        var project = TestData.RefMachine();
        new PutConnectionKind(Profinet()).Execute(project);

        CommandAssert.Rejected(project, new PutConnectionKind(Profinet() with { Name = " " }), "kind.ad_gecersiz");
        CommandAssert.Rejected(project, new PutConnectionKind(Profinet() with { Name = "Profinet 2", Color = "yeşil" }), "kind.renk_gecersiz");
        CommandAssert.Rejected(project, new PutConnectionKind(Profinet() with { Name = "PROFINET" }), "kind.ad_tekrar");
        CommandAssert.Rejected(project, new PutConnectionKind(Profinet() with { Name = "X", Style = (LineStyle)9 }), "kind.deger_gecersiz");
    }

    [Fact]
    public void A_connection_gets_a_kind_and_an_unknown_kind_is_rejected()
    {
        var project = TestData.RefMachine();
        var kind = Profinet();
        new PutConnectionKind(kind).Execute(project);
        var connection = project.Connections[0];

        CommandAssert.RoundTrips(project, new SetConnectionKind(connection.Id, kind.Id));
        Assert.Equal(kind.Id, connection.KindId);

        CommandAssert.RoundTrips(project, new SetConnectionKind(connection.Id, null));
        Assert.Null(connection.KindId);
        CommandAssert.Rejected(project, new SetConnectionKind(connection.Id, ConnectionKindId.New()), "kind.yok");
        CommandAssert.Rejected(project, new SetConnectionKind(ConnectionId.New(), kind.Id), "connection.yok");
    }

    [Fact]
    public void Removing_a_kind_leaves_its_connections_without_a_kind_and_undo_gives_them_back()
    {
        var project = TestData.RefMachine();
        var kind = Profinet();
        new PutConnectionKind(kind).Execute(project);
        new SetConnectionKind(project.Connections[0].Id, kind.Id).Execute(project);
        new SetConnectionKind(project.Connections[1].Id, kind.Id).Execute(project);

        CommandAssert.RoundTrips(project, new RemoveConnectionKind(kind.Id));

        Assert.Empty(project.ConnectionKinds);
        Assert.All(project.Connections, connection => Assert.Null(connection.KindId));
        CommandAssert.Rejected(project, new RemoveConnectionKind(kind.Id), "kind.yok");
    }

    [Fact]
    public void Standard_kinds_are_valid_unique_and_keep_their_ids()
    {
        var all = ConnectionKind.Standard;

        Assert.All(all, kind => Assert.Null(kind.Check()));
        Assert.Equal(all.Count, all.Select(kind => kind.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(kind => kind.Name).Distinct().Count());
        Assert.Contains(all, kind => kind is { Name: "Profinet", Style: LineStyle.Solid });
        Assert.Contains(all, kind => kind is { Name: "DriveCLiQ", Style: LineStyle.Dashed });

        // Pnömatik hatlar (ISO 1219-1): besleme düz, pilot kesik, egzoz noktalı.
        Assert.Equal(
            [("Hava hattı", LineStyle.Solid), ("Pilot hattı", LineStyle.Dashed), ("Egzoz hattı", LineStyle.Dotted)],
            all.Where(kind => kind.Category == PinKind.Pneumatic).Select(kind => (kind.Name, kind.Style)));
    }

    [Fact]
    public void Splitting_a_wire_with_a_kind_gives_both_halves_the_kind()
    {
        var project = TestData.RefMachine();
        var kind = Profinet();
        new PutConnectionKind(kind).Execute(project);
        var wire = project.Connections.First(connection => connection.Wire?.Number is not null && connection.CoreId is null);
        new SetConnectionKind(wire.Id, kind.Id).Execute(project);

        var split = Junctions.Split(project, wire.Id, project.Pages[0].Id, 50, 70).Value;
        CommandAssert.RoundTrips(project, split.Command);

        var halves = project.Connections.Where(connection => connection.From == split.Point || connection.To == split.Point).ToList();
        Assert.Equal(2, halves.Count);
        Assert.All(halves, half => Assert.Equal(kind.Id, half.KindId));
    }
}
