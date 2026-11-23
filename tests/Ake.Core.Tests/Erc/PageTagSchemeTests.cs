using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Core.Tests.Commands;
using Ake.Core.Tests.Model;

namespace Ake.Core.Tests.Erc;

/// <summary>Sayfa bazlı tag şeması: <c>3K1</c> = 3. sayfa, K, sayfadaki ilk K (ADR-0021).</summary>
public class PageTagSchemeTests
{
    private static Placement Place(Page page, Device device, double x, double y) =>
        new(PlacementId.New(), page.Id, device.Functions[0].Id, PlacementRole.Main, x, y);

    /// <summary>İki sayfa; cihazlar verilen sayfaya ve konuma yerleşir, yerleşimi olmayanlar hiçbir sayfada değildir.</summary>
    private static Project Pages(params (Device Device, int Page, double X, double Y)[] placed)
    {
        Page[] pages = [new(PageId.New(), "3", "Güç"), new(PageId.New(), "7", "Kontrol")];
        var devices = placed.Select(entry => entry.Device).ToList();
        return Project.Create(
            ProjectId.New(),
            "p",
            devices: devices,
            pages: pages,
            placements: placed.Where(entry => entry.Page >= 0).Select(entry => Place(pages[entry.Page], entry.Device, entry.X, entry.Y)),
            tagScheme: TagScheme.PageLetterNumber).Value;
    }

    [Fact]
    public void Tags_start_with_the_page_and_count_per_letter_on_that_page()
    {
        var project = Pages(
            (Build.Device("K5", "A1"), 0, 100, 40),
            (Build.Device("F2", "1"), 0, 50, 40),
            (Build.Device("K9", "A1"), 1, 80, 40),
            (Build.Device("K12", "A1"), 0, 60, 40));

        CommandAssert.RoundTrips(project, Renumbering.Tags(project));

        Assert.Equal(["3K2", "3F1", "7K1", "3K1"], project.Devices.Select(device => device.ProductTag));
    }

    [Fact]
    public void Order_on_a_page_is_left_to_right_then_top_to_bottom()
    {
        var project = Pages(
            (Build.Device("S1", "1"), 0, 50, 120),
            (Build.Device("S2", "1"), 0, 50, 40),
            (Build.Device("S3", "1"), 0, 20, 200));

        CommandAssert.RoundTrips(project, Renumbering.Tags(project));

        Assert.Equal(["3S3", "3S2", "3S1"], project.Devices.Select(device => device.ProductTag));
    }

    [Fact]
    public void Device_on_several_pages_is_numbered_on_the_first_of_them()
    {
        var k1 = new Device(
            DeviceId.New(),
            "K1",
            [
                new Function(FunctionId.New(), FunctionKind.NoContact, [new ConnectionPoint(ConnectionPointId.New(), "13")]),
                new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")]),
            ]);
        Page[] pages = [new(PageId.New(), "3", "Güç"), new(PageId.New(), "7", "Kontrol")];
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [k1],
            pages: pages,
            placements:
            [
                new Placement(PlacementId.New(), pages[1].Id, k1.Functions[1].Id, PlacementRole.Main, 10, 10),
                new Placement(PlacementId.New(), pages[0].Id, k1.Functions[0].Id, PlacementRole.Main, 300, 10),
            ],
            tagScheme: TagScheme.PageLetterNumber).Value;

        CommandAssert.RoundTrips(project, Renumbering.Tags(project));

        Assert.Equal("3K1", k1.ProductTag);
    }

    [Fact]
    public void Devices_on_no_page_and_tags_without_a_letter_are_left_alone()
    {
        var project = Pages(
            (Build.Device("K4", "A1"), -1, 0, 0),
            (Build.Device("PE", "1"), 0, 10, 10),
            (Build.Device("100", "1"), 0, 20, 10));

        Assert.Empty(Renumbering.Tags(project).Commands);
    }

    [Fact]
    public void Tags_already_in_the_page_scheme_produce_no_changes()
    {
        var project = Pages((Build.Device("3K1", "A1"), 0, 10, 10), (Build.Device("7K1", "A1"), 1, 10, 10));

        Assert.Empty(Renumbering.Tags(project).Commands);
    }

    [Fact]
    public void Device_moved_to_another_page_takes_that_page_number()
    {
        var project = Pages((Build.Device("7K1", "A1"), 0, 10, 10));

        CommandAssert.RoundTrips(project, Renumbering.Tags(project));

        Assert.Equal("3K1", project.Devices[0].ProductTag);
    }

    [Fact]
    public void Page_number_with_letters_is_stripped_before_reading_the_letter()
    {
        Page[] pages = [new(PageId.New(), "2a", "Güç")];
        var device = Build.Device("2aK7", "A1");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [device],
            pages: pages,
            placements: [Place(pages[0], device, 10, 10)],
            tagScheme: TagScheme.PageLetterNumber).Value;

        CommandAssert.RoundTrips(project, Renumbering.Tags(project));

        Assert.Equal("2aK1", device.ProductTag);
    }

    [Fact]
    public void Changing_the_scheme_renumbers_and_undoes_in_one_step()
    {
        var project = TestData.RefMachine();
        var before = project.Devices.Select(device => device.ProductTag).ToList();
        var stack = new CommandStack(project);

        Assert.True(stack.Execute(Renumbering.ChangeTagScheme(project, TagScheme.PageLetterNumber), ChangeSource.User, "test").IsSuccess);

        Assert.Equal(TagScheme.PageLetterNumber, project.TagScheme);
        Assert.Equal("=M1+P1-2K1", project.DesignationOf(project.Devices.Single(d => d.Description == "Motor kontaktörü 1")).ToString());
        Assert.Equal("=M2+P1-4K1", project.DesignationOf(project.Devices.Single(d => d.Description == "Motor kontaktörü 2")).ToString());
        Assert.DoesNotContain(ErcEngine.Default.Run(project).Findings, finding => finding.RuleId == "erc.tag_tekrar");

        Assert.True(stack.Undo().IsSuccess);
        Assert.Equal(TagScheme.LetterNumber, project.TagScheme);
        Assert.Equal(before, project.Devices.Select(device => device.ProductTag));
    }

    [Fact]
    public void Going_back_to_letters_drops_the_page_number()
    {
        var project = Pages((Build.Device("3K2", "A1"), 0, 10, 10), (Build.Device("7K1", "A1"), 1, 10, 10));

        CommandAssert.RoundTrips(project, Renumbering.ChangeTagScheme(project, TagScheme.LetterNumber));

        Assert.Equal(TagScheme.LetterNumber, project.TagScheme);
        Assert.Equal(["K1", "K2"], project.Devices.Select(device => device.ProductTag));
    }

    [Fact]
    public void Choosing_the_current_scheme_only_renumbers()
    {
        var project = Pages((Build.Device("3K1", "A1"), 0, 10, 10));

        Assert.Empty(Renumbering.ChangeTagScheme(project, TagScheme.PageLetterNumber).Commands);
    }

    [Fact]
    public void Set_tag_scheme_round_trips_and_rejects_unknown_values()
    {
        var project = Build.Project([Build.Device("K1", "A1")]);

        CommandAssert.RoundTrips(project, new SetTagScheme(TagScheme.PageLetterNumber));
        CommandAssert.Rejected(project, new SetTagScheme((TagScheme)42), "tag.sema_gecersiz");
    }
}
