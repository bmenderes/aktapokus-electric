using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Numbering;

namespace Ake.Core.Tests.Commands;

/// <summary>Sayfa numaraları: araya alt sayfa (3.1), numara değiştirme ve baştan numaralama (ADR-0042).</summary>
public class PageNumberTests
{
    private static List<string> Numbers(Project project) => [.. project.Pages.Select(page => page.Number)];

    [Fact]
    public void The_next_sub_number_follows_the_root_of_the_page_it_comes_after_and_never_repeats()
    {
        var project = TestData.RefMachine();
        Assert.Equal(["1", "2", "3", "4", "5"], Numbers(project));

        Assert.Equal("3.1", PageNumbering.NextSubNumber(project, 2));
        Assert.Equal("5.1", PageNumbering.NextSubNumber(project, 4));

        var sub = new Page(PageId.New(), "3.1", "Ek", templateId: project.Pages[2].TemplateId);
        Assert.True(new AddPage(sub, 3).Execute(project).IsSuccess);
    }

    [Fact]
    public void An_inserted_page_takes_the_given_place_without_renumbering_the_pages_after_it_and_undoes()
    {
        var project = TestData.RefMachine();
        var before = Numbers(project);
        var page = new Page(PageId.New(), PageNumbering.NextSubNumber(project, 2), "Ek sayfa", templateId: project.Pages[2].TemplateId);

        CommandAssert.RoundTrips(project, new AddPage(page, 3));

        Assert.Equal(["1", "2", "3", "3.1", "4", "5"], Numbers(project));
        Assert.Equal(before, Numbers(project).Where(number => !number.Contains('.', StringComparison.Ordinal)).ToList());
        // Bir sonraki alt sayfa aynı ana numarada devam eder ve 3.1'den sonra 3.2 olur.
        Assert.Equal("3.2", PageNumbering.NextSubNumber(project, 3));
        Assert.Equal("3.2", PageNumbering.NextSubNumber(project, 2));
    }

    [Fact]
    public void An_insert_is_refused_for_a_bad_place_or_a_number_already_in_use()
    {
        var project = TestData.RefMachine();
        var template = project.Pages[0].TemplateId;

        CommandAssert.Rejected(project, new AddPage(new Page(PageId.New(), "9", "X", templateId: template), 99), "page.sira_gecersiz");
        CommandAssert.Rejected(project, new AddPage(new Page(PageId.New(), "3", "X", templateId: template), 1), "page.numara_tekrar");
    }

    [Fact]
    public void A_page_number_is_changed_with_a_command_that_keeps_numbers_unique()
    {
        var project = TestData.RefMachine();
        var page = project.Pages[2];

        CommandAssert.RoundTrips(project, new SetPageNumber(page.Id, " 3.1 "));
        Assert.Equal("3.1", page.Number);

        CommandAssert.Rejected(project, new SetPageNumber(page.Id, "4"), "page.numara_tekrar");
        CommandAssert.Rejected(project, new SetPageNumber(page.Id, "  "), "page.numara_gecersiz");
        CommandAssert.Rejected(project, new SetPageNumber(page.Id, "3 1"), "page.numara_gecersiz");
        CommandAssert.Rejected(project, new SetPageNumber(PageId.New(), "9"), "page.yok");
    }

    [Fact]
    public void Renumbering_from_scratch_follows_the_list_order_and_does_nothing_when_already_in_order()
    {
        var project = TestData.RefMachine();
        Assert.Empty(PageNumbering.Renumber(project).Commands);
        new AddPage(new Page(PageId.New(), "3.1", "Ek", templateId: project.Pages[2].TemplateId), 3).Execute(project);
        new AddPage(new Page(PageId.New(), "3.2", "Ek 2", templateId: project.Pages[2].TemplateId), 4).Execute(project);
        Assert.Equal(["1", "2", "3", "3.1", "3.2", "4", "5"], Numbers(project));

        CommandAssert.RoundTrips(project, PageNumbering.Renumber(project));

        Assert.Equal(["1", "2", "3", "4", "5", "6", "7"], Numbers(project));
    }

    [Fact]
    public void The_next_page_number_follows_the_highest_whole_number_and_ignores_sub_pages()
    {
        var project = TestData.RefMachine();
        Assert.Equal("6", PageNumbering.NextNumber(project));

        new AddPage(new Page(PageId.New(), "5.1", "Ek", templateId: project.Pages[4].TemplateId), 5).Execute(project);
        Assert.Equal("6", PageNumbering.NextNumber(project));

        new SetPageNumber(project.Pages[0].Id, "9").Execute(project);
        Assert.Equal("10", PageNumbering.NextNumber(project));
    }

    [Fact]
    public void A_page_moves_to_another_place_without_touching_any_number_and_undoes()
    {
        var project = TestData.RefMachine();
        var last = project.Pages[^1];

        CommandAssert.RoundTrips(project, new MovePage(last.Id, 0));

        Assert.Equal(["5", "1", "2", "3", "4"], Numbers(project)); // yalnızca sıra değişir, numaralar aynı
        Assert.Same(last, project.Pages[0]);
    }

    [Fact]
    public void Moving_in_the_middle_and_to_the_end_keeps_the_other_pages_in_order()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, new MovePage(project.Pages[1].Id, 3));
        Assert.Equal(["1", "3", "4", "2", "5"], Numbers(project));

        CommandAssert.RoundTrips(project, new MovePage(project.Pages[0].Id, 4));
        Assert.Equal(["3", "4", "2", "5", "1"], Numbers(project));
    }

    [Fact]
    public void A_move_to_a_bad_place_or_of_an_unknown_page_is_rejected()
    {
        var project = TestData.RefMachine();

        CommandAssert.Rejected(project, new MovePage(project.Pages[0].Id, 5), "page.sira_gecersiz");
        CommandAssert.Rejected(project, new MovePage(project.Pages[0].Id, -1), "page.sira_gecersiz");
        CommandAssert.Rejected(project, new MovePage(PageId.New(), 0), "page.yok");
    }
}
