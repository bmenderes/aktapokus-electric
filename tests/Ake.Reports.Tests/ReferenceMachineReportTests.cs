using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Reports.Tests;

/// <summary>
/// Faz 3 DoD: referans makinenin bütün raporları snapshot ile doğrulanır. Beklenen içerik bu
/// dosyanın yanındaki <c>*.verified.txt</c> dosyalarındadır; rapor değişirse test kırılır ve
/// değişiklik gözle onaylanmadan geçmez.
/// </summary>
public class ReferenceMachineReportTests
{
    internal static Project Machine()
    {
        var path = Path.Combine(RepositoryRoot(), "examples", "ref-machine", "ref-machine.json");
        var result = ProjectFile.Load(path);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static Task VerifyReport(string name) =>
        Verifier.Verify(CsvReport.Write(ReportCatalog.Build(name, Machine()).Value)).UseParameters(name);

    [Theory]
    [InlineData("bom")]
    [InlineData("cables")]
    [InlineData("wires")]
    [InlineData("terminals")]
    [InlineData("io")]
    [InlineData("labels")]
    public Task Report_matches_its_snapshot(string name) => VerifyReport(name);

    [Fact]
    public void Catalog_lists_every_report_it_can_build()
    {
        Assert.All(ReportCatalog.Names, name => Assert.True(ReportCatalog.Build(name, Machine()).IsSuccess));
        Assert.Equal("rapor.yok", ReportCatalog.Build("uydurma", Machine()).Error!.Code);
    }

    [Fact]
    public void Building_reports_does_not_change_the_model()
    {
        var machine = Machine();
        var before = ProjectJson.Serialize(machine);

        foreach (var name in ReportCatalog.Names)
        {
            ReportCatalog.Build(name, machine);
        }

        Assert.Equal(before, ProjectJson.Serialize(machine));
    }
}
