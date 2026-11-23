using Ake.Core.Model;

namespace Ake.Render.Tests;

/// <summary>Kontak ayna tablosu, kontakta bobin referansı ve yolu bulunamayan tel uyarısı (ADR-0019).</summary>
public class CrossReferenceRenderTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static readonly SheetTemplate Template = new(
        SheetTemplateId.New(), "A3", 420, 297, 10, 10, 6, 5, 20, "Arial", []);

    private static Function Fn(FunctionKind kind, params string[] points) =>
        new(FunctionId.New(), kind, points.Select(name => new ConnectionPoint(ConnectionPointId.New(), name)));

    private static ConnectionPointId Point(Device device, string name) =>
        device.Functions.SelectMany(function => function.ConnectionPoints).Single(point => point.Name == name).Id;

    private static Placement Place(Page page, Device device, int function, double x, double y) =>
        new(PlacementId.New(), page.Id, device.Functions[function].Id, PlacementRole.Main, x, y, 0);

    private static Scene Render(Project project, int page = 0) =>
        SheetRenderer.Render(project, project.Pages[page], Today).Value;

    private static (Project Project, Device Contactor) Contactor()
    {
        var k1 = new Device(
            DeviceId.New(),
            "K1",
            [
                Fn(FunctionKind.Coil, "A1", "A2"),
                Fn(FunctionKind.PowerContact, "1", "2"),
                Fn(FunctionKind.NoContact, "13", "14"),
                Fn(FunctionKind.NcContact, "21", "22"),
            ]);
        var power = new Page(PageId.New(), "2", "Güç");
        var control = new Page(PageId.New(), "3", "Kontrol");

        // NC kontak hiçbir sayfada değil. Kolon genişliği 39 mm: x=100 → kolon 2, x=250 → kolon 6.
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [k1],
            pages: [power, control],
            placements: [Place(control, k1, 0, 250, 60), Place(power, k1, 1, 100, 60), Place(control, k1, 2, 100, 60)],
            sheetTemplates: [Template]).Value;
        return (project, k1);
    }

    [Fact]
    public void Mirror_lines_and_the_coil_location_link_to_the_pages_they_name()
    {
        var (project, _) = Contactor();
        var power = SheetRenderer.AnchorOf(project.Pages[0]);
        var control = SheetRenderer.AnchorOf(project.Pages[1]);

        var onCoilPage = Render(project, 1).Items.OfType<LinkItem>().Select(link => link.Target).ToList();
        var onContactPage = Render(project, 0).Items.OfType<LinkItem>().Select(link => link.Target).ToList();

        // Kontrol sayfasında (bobin ve NO kontak var): ayna tablosunda güç kontağı güç sayfasına, NO kontağı kendi
        // sayfasına; NO kontağın altındaki "(3.6)" bobinin sayfasına (yine kendisi). NC yerleşimsiz, bağlantısı yok.
        Assert.Equal([power, control, control], onCoilPage);
        // Kontak sayfasında: kontağın altındaki "(3.6)" bobinin sayfasına gider.
        Assert.Equal([control], onContactPage);
    }

    [Fact]
    public void Links_cover_the_text_they_belong_to()
    {
        var (project, _) = Contactor();

        var scene = Render(project, 1);

        var mirror = scene.Items.OfType<TextItem>().Single(text => text.Text == "G  1-2   2.2");
        var link = scene.Items.OfType<LinkItem>().First();
        Assert.True(link.X <= mirror.X + 0.01 && link.X + link.Width >= mirror.X + 5);
        Assert.True(link.Y <= mirror.Y && link.Y + link.Height >= mirror.Y - 0.01);
        Assert.All(scene.Items.OfType<LinkItem>(), l => Assert.True(l.Width > 0 && l.Height > 0));
    }

    [Fact]
    public void Coil_lists_every_contact_of_its_device_with_where_it_is_drawn()
    {
        var (project, _) = Contactor();

        var texts = Render(project, 1).Items.OfType<TextItem>().Select(text => text.Text).ToList();

        Assert.Contains("G  1-2   2.2", texts);
        Assert.Contains("NO 13-14 3.2", texts);
        Assert.Contains("NC 21-22 —", texts);
    }

    [Fact]
    public void Mirror_table_stays_below_the_page_notes_of_the_coil()
    {
        var (project, _) = Contactor();

        var scene = Render(project, 1);
        var table = scene.Items.OfType<TextItem>().Single(text => text.Text.StartsWith("G ", StringComparison.Ordinal));

        // Bobin x=250, y=60..75; tablo alt ucun sağında ve en az not uzunluğu kadar aşağıda.
        Assert.True(table.X > 250);
        Assert.True(table.Y > 75 + 15);
    }

    [Fact]
    public void Contact_shows_where_its_coil_is_under_the_tag()
    {
        var (project, _) = Contactor();

        Assert.Contains(Render(project, 0).Items.OfType<TextItem>(), text => text.Text == "(3.6)");
        Assert.Contains(Render(project, 1).Items.OfType<TextItem>(), text => text.Text == "(3.6)");
    }

    [Fact]
    public void Device_without_a_coil_gets_no_mirror_table()
    {
        var s1 = new Device(DeviceId.New(), "S1", [Fn(FunctionKind.NoContact, "13", "14")]);
        var page = new Page(PageId.New(), "1", "Kontrol");
        var project = Project.Create(
            ProjectId.New(), "p", devices: [s1], pages: [page], placements: [Place(page, s1, 0, 100, 60)], sheetTemplates: [Template]).Value;

        Assert.Equal(["-S1"], Render(project).Items.OfType<TextItem>().Select(text => text.Text).Where(text => text.Contains('S')));
        Assert.DoesNotContain(Render(project).Items.OfType<TextItem>(), text => text.Text.StartsWith('('));
    }

    [Fact]
    public void Wire_without_a_free_path_is_drawn_straight_in_its_own_colour_and_reported()
    {
        var top = new Device(DeviceId.New(), "F1", [Fn(FunctionKind.Protection, "1", "2")]);
        var bottom = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.PowerContact, "1", "2")]);
        var cover = new Device(DeviceId.New(), "A9", [Fn(FunctionKind.Generic, "1", "2", "3", "4", "5", "6", "7", "8")]);
        var page = new Page(PageId.New(), "1", "Güç");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [top, bottom, cover],
            connections: [new Connection(ConnectionId.New(), Point(top, "2"), Point(bottom, "1"), new Wire(WireId.New(), "7", null, null))],
            pages: [page],
            placements: [Place(page, top, 0, 50, 20), Place(page, bottom, 0, 50, 110), Place(page, cover, 0, 40, 100)],
            sheetTemplates: [Template]).Value;

        var scene = Render(project);

        var warning = Assert.Single(scene.Warnings);
        Assert.Contains("Tel 7 (-F1:2 – -K1:1)", warning, StringComparison.Ordinal);
        Assert.Contains(scene.Items.OfType<LineItem>(), line => line.Color == "#e0007a");
    }

    [Fact]
    public void Page_whose_wires_all_find_a_path_has_no_warnings()
    {
        var (project, _) = Contactor();

        Assert.Empty(Render(project, 0).Warnings);
    }

    [Fact]
    public void A_sub_page_number_separates_the_column_with_a_slash_so_it_cannot_be_confused_with_the_page()
    {
        var (project, contactor) = Contactor();
        var coilPage = project.Pages.Single(page => project.Placements.Any(placement => placement.PageId == page.Id && placement.FunctionId == contactor.Functions[0].Id));
        Assert.True(new Ake.Core.Commands.SetPageNumber(coilPage.Id, "3.1").Execute(project).IsSuccess);

        var texts = project.Pages.SelectMany(page => Render(project, project.Pages.ToList().IndexOf(page)).Items.OfType<TextItem>()).Select(text => text.Text).ToList();

        Assert.Contains(texts, text => text.Contains("3.1/", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text.Contains("3.1.", StringComparison.Ordinal) && !text.Contains("3.1/", StringComparison.Ordinal) && char.IsDigit(text[^1]) && text.Count(ch => ch == '.') >= 2);
    }
}
