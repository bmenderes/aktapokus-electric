using Ake.Core.Macros;
using Ake.Core.Model;

namespace Ake.Library.Tests;

public sealed class StandardMacrosTests
{
    [Fact]
    public void Built_in_macros_validate_and_apply_value_sets_on_insert()
    {
        Assert.Equal(3, StandardMacros.All.Count);
        foreach (var macro in StandardMacros.All)
        {
            Assert.Null(macro.Check());
            Assert.NotNull(macro.ValueSets);
            Assert.NotEmpty(macro.ValueSets!);
        }

        var motor = StandardMacros.All.First(m => m.Name == "Motor çıkışı");
        var project = Project.Create(ProjectId.New(), "Makro Test").Value;
        var page = new Page(PageId.New(), "1", "Ana");
        Assert.True(new Ake.Core.Commands.AddPage(page).Execute(project).IsSuccess);

        var planned = MacroService.Insert(project, motor, page.Id, 20, 30, valueSetName: "4.0 kW / 8.5 A (400 V)");
        Assert.True(planned.IsSuccess, planned.Error?.Message);
        Assert.True(planned.Value.Execute(project).IsSuccess);

        var q = project.Devices.First(d => d.ProductTag.StartsWith("-Q", StringComparison.Ordinal));
        var m = project.Devices.First(d => d.ProductTag.StartsWith("-M", StringComparison.Ordinal));
        Assert.Equal("GV2ME14", q.Part?.OrderCode);
        Assert.Equal(10m, q.Functions[0].RatedCurrentA);
        Assert.Equal("4.0 kW", m.Properties["Güç"]);
    }
}
