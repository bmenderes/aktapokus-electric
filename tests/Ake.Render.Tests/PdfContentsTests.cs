using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ake.Core.Model;

namespace Ake.Render.Tests;

/// <summary>İçindekiler sayfaları, PDF içi bağlantılar ve yer imleri (ADR-0020).</summary>
public sealed class PdfContentsTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static readonly SheetTemplate Template = new(
        SheetTemplateId.New(), "A3", 420, 297, 10, 10, 6, 5, 20, "Arial", []);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-pdf-toc-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static Project WithPages(int count) => Project.Create(
        ProjectId.New(),
        "p",
        pages: [.. Enumerable.Range(1, count).Select(n => new Page(PageId.New(), n.ToString(CultureInfo.InvariantCulture), $"Sayfa {n}"))],
        sheetTemplates: [Template]).Value;

    private static List<Scene> Pages(Project project) =>
        [.. project.Pages.Select(page => SheetRenderer.Render(project, page, Today).Value)];

    [Fact]
    public void Contents_lists_every_page_and_links_each_row_to_it()
    {
        var project = WithPages(3);

        var scene = Assert.Single(SheetRenderer.Contents(project, Today).Value);

        var texts = scene.Items.OfType<TextItem>().Select(text => text.Text).ToList();
        Assert.Contains("İçindekiler", texts);
        Assert.Contains("Sayfa 2", texts);
        Assert.Equal(
            project.Pages.Select(SheetRenderer.AnchorOf),
            scene.Items.OfType<LinkItem>().Select(link => link.Target));
        Assert.Equal("İçindekiler", scene.Bookmark);
    }

    [Fact]
    public void Long_contents_continue_on_further_sheets()
    {
        var project = WithPages(90);

        var scenes = SheetRenderer.Contents(project, Today).Value;

        Assert.True(scenes.Count > 1);
        Assert.Equal(90, scenes.Sum(scene => scene.Items.OfType<LinkItem>().Count()));
        Assert.Equal(["İçindekiler"], scenes.Select(scene => scene.Bookmark).OfType<string>());
        Assert.All(scenes.SelectMany(scene => scene.Items.OfType<LinkItem>()), link => Assert.InRange(link.Y + link.Height, 0, 297 - 20));
    }

    [Fact]
    public void Contents_needs_pages_and_a_template()
    {
        var empty = Project.Create(ProjectId.New(), "p", sheetTemplates: [Template]).Value;
        var noTemplate = Project.Create(ProjectId.New(), "p", pages: [new Page(PageId.New(), "1", "a")]).Value;

        Assert.Equal("render.sayfa_yok", SheetRenderer.Contents(empty, Today).Error!.Code);
        Assert.Equal("render.sablon_yok", SheetRenderer.Contents(noTemplate, Today).Error!.Code);
    }

    [Fact]
    public void Each_page_scene_carries_its_bookmark_and_anchor()
    {
        var project = WithPages(1);

        var scene = Pages(project)[0];

        Assert.Equal("1  Sayfa 1", scene.Bookmark);
        Assert.Equal($"sayfa-{project.Pages[0].Id}", scene.Anchor);
    }

    [Fact]
    public void Pdf_gets_bookmarks_in_page_order_through_an_incremental_update()
    {
        var project = WithPages(3);
        var scenes = SheetRenderer.Contents(project, Today).Value.Concat(Pages(project)).ToList();
        var path = Path.Combine(_directory.FullName, "cikti.pdf");

        Assert.True(PdfExporter.Save(scenes, path, null, "t").IsSuccess);

        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));

        // İki xref tablosu: SkiaSharp'ınki ve ona /Prev ile bağlı güncelleme.
        Assert.Equal(2, Regex.Count(text, @"(?m)^xref$"));
        Assert.Matches(@"/Prev \d+>>\s*startxref\s*\d+\s*%%EOF\s*$", text);
        Assert.Contains("/PageMode /UseOutlines", text);

        // Başlıklar UTF-16BE: "İçindekiler", sonra her sayfa.
        string[] titles = ["İçindekiler", "1  Sayfa 1", "2  Sayfa 2", "3  Sayfa 3"];
        var found = Regex.Matches(text, @"/Title <FEFF([0-9A-F]+)>")
            .Select(match => Encoding.BigEndianUnicode.GetString(Convert.FromHexString(match.Groups[1].Value)));
        Assert.Equal(titles, found);

        // Yer imlerinin hedefleri, sayfa ağacındaki sayfalar ve aynı sırada.
        var kids = Regex.Match(text, @"/Kids \[([^\]]*)\]").Groups[1].Value;
        var pageRefs = Regex.Matches(kids, @"(\d+) 0 R").Select(match => match.Groups[1].Value).ToList();
        var destinations = Regex.Matches(text, @"/Dest \[(\d+) 0 R /Fit\]").Select(match => match.Groups[1].Value);
        Assert.Equal(pageRefs, destinations);
    }

    [Fact]
    public void Every_offset_in_the_new_xref_table_points_at_its_object()
    {
        var project = WithPages(2);
        var path = Path.Combine(_directory.FullName, "cikti.pdf");
        Assert.True(PdfExporter.Save(Pages(project), path, null, "t").IsSuccess);

        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var start = int.Parse(Regex.Match(text, @"startxref\s+(\d+)\s+%%EOF\s*$").Groups[1].Value, CultureInfo.InvariantCulture);
        var table = text[start..text.IndexOf("trailer", start, StringComparison.Ordinal)];
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1..];

        var checkedObjects = 0;
        for (var i = 0; i < lines.Length;)
        {
            var header = lines[i].Split(' ');
            var first = int.Parse(header[0], CultureInfo.InvariantCulture);
            var count = int.Parse(header[1], CultureInfo.InvariantCulture);
            for (var k = 0; k < count; k++)
            {
                var offset = int.Parse(lines[i + 1 + k][..10], CultureInfo.InvariantCulture);
                Assert.StartsWith($"{first + k} 0 obj", text[offset..], StringComparison.Ordinal);
                checkedObjects++;
            }

            i += count + 1;
        }

        Assert.Equal(1 + 1 + 2, checkedObjects);
    }

    [Fact]
    public void Pdf_without_bookmarks_is_left_as_skia_wrote_it()
    {
        var path = Path.Combine(_directory.FullName, "cikti.pdf");
        var scene = new Scene(100, 100, [new LineItem(0, 0, 10, 10)]);

        Assert.True(PdfExporter.Save([scene], path, null, "t").IsSuccess);

        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.Equal(1, Regex.Count(text, @"(?m)^xref$"));
        Assert.DoesNotContain("/Outlines", text);
    }

    [Fact]
    public void Contents_rows_become_links_to_named_destinations_in_the_pdf()
    {
        var project = WithPages(2);
        var scenes = SheetRenderer.Contents(project, Today).Value.Concat(Pages(project)).ToList();
        var path = Path.Combine(_directory.FullName, "cikti.pdf");

        Assert.True(PdfExporter.Save(scenes, path, null, "t").IsSuccess);

        var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.Equal(2, Regex.Count(text, @"/Subtype /Link"));
        Assert.Contains("/Dests", text);
        Assert.Contains($"sayfa-{project.Pages[1].Id}", text);
    }
}
