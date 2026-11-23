using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

public class PageCommandTests
{
    private static StructureNodeId Node(Project project, Aspect aspect, string name) =>
        project.Structure.Single(node => node.Aspect == aspect && node.Name == name).Id;

    [Fact]
    public void AddPage_appends_a_page()
    {
        var project = TestData.RefMachine();
        var page = new Page(
            PageId.New(), "6", "Yedek", Node(project, Aspect.Function, "M1"), Node(project, Aspect.Location, "P1"));

        CommandAssert.RoundTrips(project, new AddPage(page));

        Assert.Same(page, project.Pages[^1]);
        Assert.Equal(6, project.Pages.Count);
    }

    [Fact]
    public void AddPage_rejects_invalid_pages()
    {
        var project = TestData.RefMachine();
        var location = Node(project, Aspect.Location, "P1");

        CommandAssert.Rejected(project, new AddPage(new Page(PageId.New(), " ", "Boş")), "page.numara_bos");
        CommandAssert.Rejected(project, new AddPage(new Page(project.Pages[0].Id, "9", "Çakışan")), "id.tekrar");
        CommandAssert.Rejected(
            project, new AddPage(new Page(PageId.New(), "9", "Düğümsüz", StructureNodeId.New())), "structure.dugum_yok");
        CommandAssert.Rejected(
            project, new AddPage(new Page(PageId.New(), "9", "Yanlış aspect", location)), "structure.aspect_uyumsuz");
        CommandAssert.Rejected(
            project,
            new AddPage(new Page(PageId.New(), "9", "Şablonsuz", templateId: SheetTemplateId.New())),
            "page.sablon_yok");
    }

    [Fact]
    public void AddPlacement_adds_a_reference_view_without_touching_connections()
    {
        var project = TestData.RefMachine();
        var coil = project.Function("K1", "A1");
        var placement = new Placement(
            PlacementId.New(), project.Pages[0].Id, coil.Id, PlacementRole.Reference, 50, 60, 90);

        CommandAssert.RoundTrips(project, new AddPlacement(placement));

        Assert.Equal(69, project.Placements.Count);
        Assert.Equal(65, project.Connections.Count);
    }

    [Fact]
    public void AddPlacement_rejects_invalid_placements()
    {
        var project = TestData.RefMachine();
        var page = project.Pages[0].Id;
        var coil = project.Function("K1", "A1").Id;

        Placement Make(PlacementId? id = null, PageId? onPage = null, FunctionId? of = null, PlacementRole role = PlacementRole.Reference, int rotation = 0) =>
            new(id ?? PlacementId.New(), onPage ?? page, of ?? coil, role, 0, 0, rotation);

        CommandAssert.Rejected(project, new AddPlacement(Make(role: PlacementRole.Main)), "placement.ana_tekrar");
        CommandAssert.Rejected(project, new AddPlacement(Make(onPage: PageId.New())), "placement.sayfa_yok");
        CommandAssert.Rejected(project, new AddPlacement(Make(of: FunctionId.New())), "placement.function_yok");
        CommandAssert.Rejected(project, new AddPlacement(Make(rotation: 45)), "placement.donus_gecersiz");
        CommandAssert.Rejected(
            project, new AddPlacement(Make(id: new PlacementId(project.Device("K1").Id.Value))), "id.tekrar");
    }

    [Fact]
    public void SetPageDetails_updates_title_structure_and_template_with_undo_and_validation()
    {
        var project = TestData.RefMachine();
        var target = project.Pages[0];
        var m1 = Node(project, Aspect.Function, "M1");
        var p1 = Node(project, Aspect.Location, "P1");
        var template = StandardSheetTemplates.All[0];
        Assert.True(new PutSheetTemplate(template).Execute(project).IsSuccess);

        CommandAssert.RoundTrips(project, new SetPageDetails(target.Id, "Ana Giriş", m1, p1, template.Id));

        Assert.Equal(("Ana Giriş", m1, p1, template.Id), (target.Title, target.FunctionNodeId, target.LocationNodeId, target.TemplateId));

        CommandAssert.Rejected(project, new SetPageDetails(PageId.New(), "X", null, null, null), "page.yok");
        CommandAssert.Rejected(project, new SetPageDetails(target.Id, "   ", null, null, null), "page.baslik_bos");
        CommandAssert.Rejected(project, new SetPageDetails(target.Id, "X", p1, null, null), "structure.aspect_uyumsuz");
        CommandAssert.Rejected(project, new SetPageDetails(target.Id, "X", null, null, SheetTemplateId.New()), "page.sablon_yok");
    }
}
