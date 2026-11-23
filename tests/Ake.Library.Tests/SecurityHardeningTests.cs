using System.IO.Compression;

namespace Ake.Library.Tests;

public sealed class SecurityHardeningTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("ake-sec-");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Qet_and_manufacturer_xml_parsers_ignore_dtd_and_xxe_entities()
    {
        var secretPath = Path.Combine(_dir.FullName, "gizli.txt");
        File.WriteAllText(secretPath, "GIZLI_ICERIK_12345");
        var secretUri = new Uri(secretPath).AbsoluteUri;

        var qetXxe = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE definition [<!ENTITY xxe SYSTEM "{secretUri}">]>
            <definition type="element" version="0.80" width="20" height="20" hotspot_x="10" hotspot_y="10">
              <names><name lang="en">&xxe;</name></names>
              <informations>CC BY 4.0</informations>
              <description><line x1="-10" y1="0" x2="10" y2="0" /></description>
            </definition>
            """;
        var qetResult = QElectroTechImport.FromXml(qetXxe, "xxe.elmt", "MIT");
        Assert.True(qetResult.IsFailure);
        Assert.Equal("qet.xml_gecersiz", qetResult.Error!.Code);
        Assert.DoesNotContain("GIZLI_ICERIK_12345", qetResult.Error.Message, StringComparison.Ordinal);

        var mfgXxe = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE root [<!ENTITY xxe SYSTEM "{secretUri}">]>
            <root><item>&xxe;</item></root>
            """;
        var mfgResult = IoddImporter.FromXml(mfgXxe, "Test", "MIT");
        Assert.True(mfgResult.IsFailure);
        Assert.Equal("kutuphane.xml_hatali", mfgResult.Error!.Code);
        Assert.DoesNotContain("GIZLI_ICERIK_12345", mfgResult.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_dxf_zip_fails_cleanly_without_leaving_temp_files()
    {
        var zipPath = Path.Combine(_dir.FullName, "bozuk.dxf.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("test.dxf");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not a valid dxf file");
        }

        var beforeTemp = Directory.GetFiles(Path.GetTempPath(), "ake-cad-*");
        var inspected = DxfImport.Inspect(zipPath);
        var afterTemp = Directory.GetFiles(Path.GetTempPath(), "ake-cad-*");

        Assert.True(inspected.IsFailure);
        Assert.Equal("dxf.okunamadi", inspected.Error!.Code);
        Assert.Equal(beforeTemp.Length, afterTemp.Length);
    }
}
