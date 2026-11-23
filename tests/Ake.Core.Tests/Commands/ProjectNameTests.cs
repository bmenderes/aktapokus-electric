using Ake.Core.Commands;

namespace Ake.Core.Tests.Commands;

public class ProjectNameTests
{
    [Fact]
    public void The_project_name_is_trimmed_changed_and_restored_by_undo()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, new SetProjectName("  Yeni ad  "));

        Assert.Equal("Yeni ad", project.Name);
    }

    [Fact]
    public void A_blank_name_is_rejected()
    {
        var project = TestData.RefMachine();

        CommandAssert.Rejected(project, new SetProjectName("   "), "proje.ad_bos");
    }
}
