using Ake.Core.Model;

namespace Ake.Serialization.Tests;

/// <summary>Hazır sayfa şablonları (ADR-0049).</summary>
public class StandardSheetTemplateTests
{
    [Fact]
    public void Every_standard_template_is_valid_and_has_a_unique_fixed_id_and_name()
    {
        var all = StandardSheetTemplates.All;

        Assert.Equal(5, all.Count);
        Assert.All(all, template => Assert.Null(template.Check()));
        Assert.Equal(all.Count, all.Select(template => template.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(template => template.Name).Distinct().Count());
        Assert.All(all, template => Assert.Same(template, StandardSheetTemplates.Find(template.Id)));
        Assert.Null(StandardSheetTemplates.Find(SheetTemplateId.New()));
    }

    [Fact]
    public void The_sizes_are_the_standard_paper_sizes_in_both_orientations()
    {
        var sizes = StandardSheetTemplates.All.Select(template => (template.Name.Split(',')[0], template.WidthMm, template.HeightMm)).ToList();

        Assert.Equal(
            [("A4 yatay", 297d, 210d), ("A4 dikey", 210d, 297d), ("A3 yatay", 420d, 297d), ("A3 dikey", 297d, 420d), ("A2 yatay", 594d, 420d)],
            sizes);
    }

    [Fact]
    public void Every_template_carries_the_same_title_block_content_so_pages_look_alike_on_any_paper()
    {
        var wanted = new[]
        {
            "{page.title}", "{project.name}", "{project.date}", "{project.drawingNo}", "{page.function}", "{page.location}",
            "{project.drawnBy}", "{project.revision}", "{page.number}", "{page.count}",
        };

        foreach (var template in StandardSheetTemplates.All)
        {
            var contents = template.Cells.Select(cell => cell.Content).OfType<string>().ToHashSet();
            Assert.All(wanted, content => Assert.Contains(content, contents));
            Assert.Contains(template.Cells, cell => cell.ImageRef == "logo.png");
        }
    }

    [Fact]
    public void The_default_template_is_the_a3_landscape_example_file()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        var fromFile = SheetTemplateJson.Load(Path.Combine(directory!.FullName, "examples", "templates", "a3-yatay.sablon.json")).Value;

        Assert.Equal(SheetTemplateJson.Serialize(fromFile), SheetTemplateJson.Serialize(StandardSheetTemplates.A3Landscape));
    }
}
