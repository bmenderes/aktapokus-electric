using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

public class SheetCommandTests
{
    private static SheetTemplate Template(string name = "A3", SheetTemplateId? id = null) => new(
        id ?? SheetTemplateId.New(),
        name,
        WidthMm: 420,
        HeightMm: 297,
        MarginMm: 10,
        Columns: 10,
        Rows: 6,
        GridBandMm: 5,
        TitleBlockHeightMm: 20,
        FontFamily: "Arial",
        Cells: [new TitleBlockCell(0, 0, 100, 20, "Proje:", "{project.name}")]);

    [Fact]
    public void PutSheetTemplate_adds_a_new_template()
    {
        var project = TestData.RefMachine();
        var before = project.SheetTemplates.Count;

        CommandAssert.RoundTrips(project, new PutSheetTemplate(Template("Kapak")));

        Assert.Equal(before + 1, project.SheetTemplates.Count);
        Assert.Equal("Kapak", project.SheetTemplates[^1].Name);
    }

    [Fact]
    public void PutSheetTemplate_replaces_a_template_with_the_same_id_in_place()
    {
        var project = TestData.RefMachine();
        var existing = project.SheetTemplates[0];
        var before = project.SheetTemplates.Count;

        CommandAssert.RoundTrips(project, new PutSheetTemplate(existing with { Name = "Yeni ad", Columns = 8 }));

        Assert.Equal(before, project.SheetTemplates.Count);
        Assert.Equal("Yeni ad", project.SheetTemplates[0].Name);
        Assert.Equal(8, project.TemplateFor(project.Pages[0])!.Columns);
    }

    [Fact]
    public void PutSheetTemplate_rejects_templates_that_cannot_be_drawn()
    {
        var project = TestData.RefMachine();

        CommandAssert.Rejected(project, new PutSheetTemplate(Template() with { Columns = 0 }), "sablon.gecersiz");
        CommandAssert.Rejected(project, new PutSheetTemplate(Template() with { Rows = 27 }), "sablon.gecersiz");
        CommandAssert.Rejected(project, new PutSheetTemplate(Template() with { WidthMm = 0 }), "sablon.gecersiz");
        CommandAssert.Rejected(project, new PutSheetTemplate(Template() with { FontFamily = " " }), "sablon.gecersiz");
        CommandAssert.Rejected(
            project, new PutSheetTemplate(Template() with { TitleBlockHeightMm = 280 }), "sablon.gecersiz");
        CommandAssert.Rejected(
            project,
            new PutSheetTemplate(Template() with { Cells = [new TitleBlockCell(350, 0, 100, 20)] }),
            "sablon.gecersiz");
    }

    [Fact]
    public void PutSheetTemplate_rejects_an_id_used_by_another_kind_of_object()
    {
        var project = TestData.RefMachine();
        var clash = Template(id: new SheetTemplateId(project.Device("K1").Id.Value));

        CommandAssert.Rejected(project, new PutSheetTemplate(clash), "id.tekrar");
    }

    [Fact]
    public void SetProjectProperty_sets_overwrites_and_removes()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, new SetProjectProperty("customer", "Örnek A.Ş."));
        Assert.Equal("Örnek A.Ş.", project.Properties["customer"]);

        CommandAssert.RoundTrips(project, new SetProjectProperty("customer", "Başka"));
        Assert.Equal("Başka", project.Properties["customer"]);

        CommandAssert.RoundTrips(project, new SetProjectProperty("customer", null));
        Assert.False(project.Properties.ContainsKey("customer"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("iki kelime")]
    [InlineData("süs{lü}")]
    public void SetProjectProperty_rejects_keys_that_could_not_be_used_as_placeholders(string key)
    {
        CommandAssert.Rejected(TestData.RefMachine(), new SetProjectProperty(key, "x"), "proje.alan_gecersiz");
    }

    [Fact]
    public void Properties_are_kept_in_key_order()
    {
        var project = Project.Create(
            ProjectId.New(), "p", properties: [new("zeta", "1"), new("alfa", "2"), new("Beta", "3")]).Value;

        Assert.Equal(["Beta", "alfa", "zeta"], project.Properties.Keys);
    }

    [Fact]
    public void Page_pointing_to_a_missing_template_is_rejected()
    {
        var page = new Page(PageId.New(), "1", "Sayfa", templateId: SheetTemplateId.New());

        Assert.Equal("page.sablon_yok", Project.Create(ProjectId.New(), "p", pages: [page]).Error!.Code);
    }

    [Fact]
    public void Page_without_its_own_template_uses_the_first_one()
    {
        var first = Template("Birinci");
        var second = Template("İkinci");
        var plain = new Page(PageId.New(), "1", "Varsayılan");
        var special = new Page(PageId.New(), "2", "Özel", templateId: second.Id);
        var project = Project.Create(
            ProjectId.New(), "p", pages: [plain, special], sheetTemplates: [first, second]).Value;

        Assert.Same(first, project.TemplateFor(plain));
        Assert.Same(second, project.TemplateFor(special));
    }
}
