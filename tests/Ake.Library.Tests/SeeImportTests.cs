using System.Text;
using Ake.Core.Model;
using ClosedXML.Excel;

namespace Ake.Library.Tests;

public sealed class SeeImportTests
{
    [Fact]
    public void Imports_four_engineering_sheets_and_real_sample_when_present()
    {
        var temp = Directory.CreateTempSubdirectory("ake-see-list-");
        try
        {
            var xlsxPath = Path.Combine(temp.FullName, "sample-io.xlsx");
            using (var wb = new XLWorkbook())
            {
                var motors = wb.Worksheets.Add("MOTOR LİSTESİ");
                motors.Cell(1, 1).Value = "Sürücü İsmi";
                motors.Cell(1, 2).Value = "Sürücü Tipi";
                motors.Cell(1, 3).Value = "Sercos";
                motors.Cell(1, 4).Value = "İsim";
                motors.Cell(1, 5).Value = "Kullanılan Motorlar";
                motors.Cell(1, 6).Value = "Redüktör";
                motors.Cell(1, 7).Value = "KW";
                motors.Cell(2, 1).Value = "D_Besleme";
                motors.Cell(2, 2).Value = "LXM62";
                motors.Cell(2, 3).Value = "101";
                motors.Cell(2, 4).Value = "Besleme Ekseni";
                motors.Cell(2, 5).Value = "SH100";
                motors.Cell(2, 6).Value = "i=10";
                motors.Cell(2, 7).Value = "1.5";

                var islands = wb.Worksheets.Add("VALF ADASI");
                islands.Cell(1, 2).Value = "ADA1";
                islands.Cell(1, 3).Value = "P1";
                islands.Cell(1, 4).Value = "VTUG";
                islands.Cell(2, 1).Value = "Ham";
                islands.Cell(2, 2).Value = "Port";
                islands.Cell(2, 3).Value = "Signal Name";
                islands.Cell(2, 4).Value = "Etiket";
                islands.Cell(2, 5).Value = "Tip";
                islands.Cell(3, 1).Value = "X0";
                islands.Cell(3, 2).Value = "1";
                islands.Cell(3, 3).Value = "Giriş valfi";
                islands.Cell(3, 4).Value = "-Y1";
                islands.Cell(3, 5).Value = "V1";
                islands.Cell(4, 1).Value = "X1";
                islands.Cell(4, 2).Value = "2";
                islands.Cell(4, 3).Value = "Çıkış valfi";
                islands.Cell(4, 4).Value = "-Y2";
                islands.Cell(4, 5).Value = "V2";

                var lmc = wb.Worksheets.Add("PLC IO");
                lmc.Cell(1, 1).Value = "TM5SDI12D";
                lmc.Cell(2, 1).Value = "Klemens";
                lmc.Cell(2, 2).Value = "Nr";
                lmc.Cell(2, 3).Value = "İsim";
                lmc.Cell(2, 4).Value = "Kod";
                lmc.Cell(3, 1).Value = "X1:1";
                lmc.Cell(3, 2).Value = "%IX0.0";
                lmc.Cell(3, 3).Value = "Acil Stop";
                lmc.Cell(3, 4).Value = "-S1";

                var drv = wb.Worksheets.Add("SÜRÜCÜ I.O LİSTESİ");
                drv.Cell(1, 1).Value = "LXM62 D_Besleme";
                drv.Cell(2, 1).Value = "Nr";
                drv.Cell(2, 2).Value = "İsim";
                drv.Cell(2, 3).Value = "Kod";
                drv.Cell(2, 4).Value = "Klemens";
                drv.Cell(3, 1).Value = "DI0";
                drv.Cell(3, 2).Value = "Enable";
                drv.Cell(3, 3).Value = "D_Besleme";
                drv.Cell(3, 4).Value = "X2:3";

                wb.SaveAs(xlsxPath);
            }

            var project = Project.Create(ProjectId.New(), "SEE İçe Aktarım").Value;
            var imported = SeeListImport.FromFile(project, xlsxPath);
            Assert.True(imported.IsSuccess, imported.Error?.Message);
            var exec = imported.Value.Transaction.Execute(project);
            Assert.True(exec.IsSuccess, exec.Error?.Message);
            Assert.True(imported.Value.Summary.DevicesAdded > 0);
            Assert.True(imported.Value.Summary.ChannelsAdded >= 2);
            Assert.Contains(
                project.Controllers.SelectMany(c => c.Networks).SelectMany(n => n.Nodes).SelectMany(n => n.Modules).SelectMany(m => m.Channels),
                ch => ch.Address == "%IX0.0");
            Assert.Contains(project.Devices, d => d.Properties.GetValueOrDefault("powerKw") == "1.5");
            Assert.Contains(project.Devices, d => d.Properties.GetValueOrDefault("island") == "ADA1");

            // CSV listesi içe aktarma
            var csvPath = Path.Combine(temp.FullName, "io.csv");
            File.WriteAllText(csvPath, "Klemens;Nr;İsim;Kod\nX5:1;%QX1.0;Start Lamba;-H1\n", Encoding.UTF8);
            var csvRes = SeeListImport.FromFile(project, csvPath);
            Assert.True(csvRes.IsSuccess, csvRes.Error?.Message);
            Assert.True(csvRes.Value.Transaction.Execute(project).IsSuccess);

            // Ornek altındaki gerçek Excel dosyası varsa ayrıştırmayı test et.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                dir = dir.Parent;
            }

            var realExcel = Path.Combine(dir!.FullName, "..", "Ornek", "122613DW Detay Kahve Dual Wraparound IO.xlsx");
            if (File.Exists(realExcel))
            {
                var realProject = Project.Create(ProjectId.New(), "Gerçek Örnek").Value;
                var realRes = SeeListImport.FromFile(realProject, realExcel);
                Assert.True(realRes.IsSuccess, realRes.Error?.Message);
                Assert.True(realRes.Value.Transaction.Execute(realProject).IsSuccess);
                Assert.True(realRes.Value.Summary.DevicesAdded >= 10);
                Assert.True(realRes.Value.Summary.ChannelsAdded >= 10);
            }
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
