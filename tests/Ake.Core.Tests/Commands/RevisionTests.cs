using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Numbering;

namespace Ake.Core.Tests.Commands;

/// <summary>Revizyon geçmişi (ADR-0050).</summary>
public class RevisionTests
{
    [Fact]
    public void A_revision_is_added_to_the_history_and_undone()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, new AddRevision(new Revision("A", "15.11.2026", "AKE", "İlk yayın")));

        Assert.Equal(["A"], project.Revisions.Select(revision => revision.Mark));
    }

    [Fact]
    public void A_bad_mark_an_empty_description_or_a_repeated_mark_is_rejected()
    {
        var project = TestData.RefMachine();
        Assert.True(new AddRevision(new Revision("A", "", "", "ilk")).Execute(project).IsSuccess);

        CommandAssert.Rejected(project, new AddRevision(new Revision(" ", "", "", "x")), "revizyon.isaret_gecersiz");
        CommandAssert.Rejected(project, new AddRevision(new Revision("A B", "", "", "x")), "revizyon.isaret_gecersiz");
        CommandAssert.Rejected(project, new AddRevision(new Revision("ABCDEFGHI", "", "", "x")), "revizyon.isaret_gecersiz");
        CommandAssert.Rejected(project, new AddRevision(new Revision("B", "", "", "  ")), "revizyon.aciklama_bos");
        CommandAssert.Rejected(project, new AddRevision(new Revision("A", "", "", "yine")), "revizyon.isaret_tekrar");
    }

    [Theory]
    [InlineData(new string[0], "A")]
    [InlineData(new[] { "A" }, "B")]
    [InlineData(new[] { "A", "B" }, "C")]
    [InlineData(new[] { "Y" }, "Z")]
    [InlineData(new[] { "Z" }, "AA")]
    [InlineData(new[] { "AZ" }, "BA")]
    [InlineData(new[] { "ZZ" }, "AAA")]
    [InlineData(new[] { "1" }, "2")]
    [InlineData(new[] { "9" }, "10")]
    public void The_next_revision_mark_continues_the_last_one(string[] existing, string expected)
    {
        var project = TestData.RefMachine();
        foreach (var mark in existing)
        {
            Assert.True(new AddRevision(new Revision(mark, "", "", "x")).Execute(project).IsSuccess);
        }

        Assert.Equal(expected, RevisionNumbering.NextMark(project));
    }
}
