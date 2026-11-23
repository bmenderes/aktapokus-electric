using System.IO.Compression;
using Ake.Core.Model;

namespace Ake.Library.Tests;

public sealed class DxfPageAndPacDriveTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    [Fact]
    public void Pacdrive_sample_dxf_inspects_two_views_crops_front_and_imports_zip_and_page()
    {
        var repoRoot = FindRepoRoot();
        var dxfPath = Path.Combine(repoRoot, "examples", "dxf", "pacdrive-lxm62.dxf");
        Assert.True(File.Exists(dxfPath), $"Örnek PacDrive DXF bulunamadı: {dxfPath}");

        var inspected = DxfImport.Inspect(dxfPath);
        Assert.True(inspected.IsSuccess, inspected.Error?.Message);
        Assert.NotNull(inspected.Value.Extents);
        Assert.Equal(4, inspected.Value.Points);
        Assert.True(inspected.Value.Extents!.Value.Width >= 170);
        Assert.Contains(inspected.Value.Layers, l => l.Name == "OUTLINE");

        // Yalnızca ön görünüşü (0..65) kırparak sembol al; "SIDE" yazısı dışarıda kalmalı.
        var front = DxfImport.FromPath(dxfPath, "Apache-2.0", area: (0, 0, 65, 120));
        var frontSymbol = Assert.Single(front);
        Assert.Null(frontSymbol.Error);
        Assert.NotNull(frontSymbol.Symbol);
        Assert.Contains(frontSymbol.Symbol!.Shapes.OfType<SymbolText>(), t => t.Text == "LXM62");
        Assert.DoesNotContain(frontSymbol.Symbol!.Shapes.OfType<SymbolText>(), t => t.Text == "SIDE");

        // .dxf.zip arşivi içinden okuma.
        var temp = Directory.CreateTempSubdirectory("ake-dxf-zip-");
        try
        {
            var zipPath = Path.Combine(temp.FullName, "pacdrive.dxf.zip");
            using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(dxfPath, "pacdrive-lxm62.dxf");
            }

            var fromZip = DxfImport.FromPath(zipPath, "Apache-2.0");
            var zipSymbol = Assert.Single(fromZip);
            Assert.Null(zipSymbol.Error);
            Assert.NotNull(zipSymbol.Symbol);

            // Tam DXF sayfasını serbest grafik (PageGraphic) olarak projeye alma.
            var drawing = DxfPageImport.Read(zipPath);
            Assert.True(drawing.IsSuccess, drawing.Error?.Message);
            var pageDrawing = Assert.Single(drawing.Value.Pages);
            Assert.NotEmpty(pageDrawing.Shapes);

            var project = Project.Create(
                ProjectId.New(),
                "DXF Sayfa",
                sheetTemplates: [StandardSheetTemplates.A3Landscape]).Value;
            var tx = DxfPageImport.Plan(project, drawing.Value);
            Assert.True(tx.IsSuccess, tx.Error?.Message);
            Assert.True(tx.Value.Execute(project).IsSuccess);
            Assert.Single(project.Pages);
            Assert.NotEmpty(project.Graphics);
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        // Gerçek Ornek/beckhoff klasörü varsa arşivlerden en az birinin incelemesini dene.
        var beckhoffDir = Path.Combine(repoRoot, "..", "Ornek", "beckhoff");
        if (Directory.Exists(beckhoffDir))
        {
            var sample = Directory.EnumerateFiles(beckhoffDir, "*.dxf.zip").Order().FirstOrDefault()
                ?? Directory.EnumerateFiles(beckhoffDir, "*.dxf").Order().FirstOrDefault();
            if (sample is not null)
            {
                var realCheck = DxfImport.Inspect(sample);
                Assert.True(realCheck.IsSuccess, realCheck.Error?.Message);
            }
        }
    }
}
