using System.IO.Compression;
using System.Text;
using Ake.Core.Model;
using Ake.Library;

namespace Ake.Library.Tests;

public sealed class ManufacturerImporterTests
{
    [Fact]
    public void Bmecat_with_etim_class_maps_contactor_functions_and_current()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BMECAT version="2005">
              <HEADER><CATALOG><CATALOG_ID>CAT1</CATALOG_ID></CATALOG><SUPPLIER><SUPPLIER_NAME>Schneider Electric</SUPPLIER_NAME></SUPPLIER></HEADER>
              <T_NEW_CATALOG>
                <ARTICLE>
                  <SUPPLIER_AID>LC1D09M7</SUPPLIER_AID>
                  <ARTICLE_DETAILS>
                    <DESCRIPTION_SHORT>TeSys D Kontaktör 9A 220VAC</DESCRIPTION_SHORT>
                    <MANUFACTURER_TYPE_DESCR>LC1D09</MANUFACTURER_TYPE_DESCR>
                  </ARTICLE_DETAILS>
                  <ARTICLE_FEATURES>
                    <REFERENCE_FEATURE_SYSTEM_NAME>ETIM-8.0</REFERENCE_FEATURE_SYSTEM_NAME>
                    <REFERENCE_FEATURE_GROUP_ID>EC000066</REFERENCE_FEATURE_GROUP_ID>
                    <FEATURE><FNAME>Nominal çalışma akımı</FNAME><FVALUE>9</FVALUE><FUNIT>A</FUNIT></FEATURE>
                  </ARTICLE_FEATURES>
                </ARTICLE>
              </T_NEW_CATALOG>
            </BMECAT>
            """;

        var result = BmecatImporter.FromXml(xml, "Schneider Kataloğu", "Üretici kataloğu");
        Assert.True(result.IsSuccess, result.Error?.Message);
        var p = Assert.Single(result.Value);
        Assert.Equal("Schneider Electric", p.Definition.Manufacturer);
        Assert.Equal("LC1D09M7", p.Definition.OrderCode);
        Assert.Equal("Kumanda / Röle", p.Category);
        Assert.Contains(p.Definition.Functions, f => f.Kind == FunctionKind.Coil);
        Assert.Equal(9m, p.Definition.Functions.First(f => f.Kind == FunctionKind.PowerContact).RatedCurrentA);
    }

    [Fact]
    public void Gsdml_imports_profinet_dap_and_io_modules()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ISO15745Profile xmlns="http://www.profibus.com/GSDML/2003/11/DeviceProfile">
              <ProfileBody>
                <DeviceIdentity VendorName="Siemens AG" VendorID="0x002A" DeviceID="0x0303">
                  <InfoText TextId="T_INFO"/>
                </DeviceIdentity>
                <ApplicationProcess>
                  <DeviceAccessPointList>
                    <DeviceAccessPointItem ID="DAP1">
                      <ModuleInfo>
                        <Name TextId="T_DAP"/>
                        <OrderNumber Value="6ES7155-6AU01-0BN0"/>
                      </ModuleInfo>
                    </DeviceAccessPointItem>
                  </DeviceAccessPointList>
                  <ModuleList>
                    <ModuleItem ID="M1">
                      <ModuleInfo>
                        <Name TextId="T_DI8"/>
                        <OrderNumber Value="6ES7131-6BF01-0BA0"/>
                      </ModuleInfo>
                      <VirtualSubmoduleList>
                        <VirtualSubmoduleItem ID="VS1">
                          <IOData>
                            <Input>
                              <DataItem TextId="T_CH0"/>
                              <DataItem TextId="T_CH1"/>
                            </Input>
                          </IOData>
                        </VirtualSubmoduleItem>
                      </VirtualSubmoduleList>
                    </ModuleItem>
                  </ModuleList>
                  <ExternalTextList>
                    <PrimaryLanguage>
                      <Text TextId="T_INFO" Value="ET 200SP IM 155-6 PN"/>
                      <Text TextId="T_DAP" Value="IM 155-6 PN ST"/>
                      <Text TextId="T_DI8" Value="DI 8x24VDC ST"/>
                      <Text TextId="T_CH0" Value="DI0"/>
                      <Text TextId="T_CH1" Value="DI1"/>
                    </PrimaryLanguage>
                  </ExternalTextList>
                </ApplicationProcess>
              </ProfileBody>
            </ISO15745Profile>
            """;

        var result = GsdmlImporter.FromXml(xml, "PROFINET GSDML", "Üretici dosyası");
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.Count);
        var ioMod = result.Value[1];
        Assert.Equal("6ES7131-6BF01-0BA0", ioMod.Definition.OrderCode);
        Assert.Equal(["DI0", "DI1"], ioMod.Definition.Functions[0].ConnectionPoints);
    }

    [Fact]
    public void Eds_iodd_aasx_edz_and_skycad_all_import_and_require_license()
    {
        var temp = Directory.CreateTempSubdirectory("ake-mfg-");
        try
        {
            // 1. EDS
            var edsPath = Path.Combine(temp.FullName, "drive.eds");
            File.WriteAllText(edsPath, """
                [Device]
                VendName = "Rockwell Automation";
                ProdName = "PowerFlex 525";
                Catalog = "25B-D4P0N104";
                MajRev = 4;
                MinRev = 1;
                """, Encoding.UTF8);
            var eds = LibraryImport.FromFile(edsPath, "ODVA EDS", "Üretici EDS");
            Assert.True(eds.IsSuccess, eds.Error?.Message);
            Assert.Equal("25B-D4P0N104", eds.Value[0].Definition.OrderCode);

            // Doğrulama: lisans yoksa reddedilir.
            Assert.True(EdsImporter.FromFile(edsPath, "ODVA", string.Empty).IsFailure);

            // 2. Zipped IODD
            var ioddZip = Path.Combine(temp.FullName, "sensor.iodd");
            using (var z = ZipFile.Open(ioddZip, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("ifm-O5D100-IODD1.1.xml");
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write("""
                    <IODevice>
                      <ProfileBody>
                        <DeviceIdentity vendorName="ifm electronic" vendorId="310" deviceId="1024">
                          <DeviceName textId="T_NAME"/>
                          <DeviceVariantCollection>
                            <DeviceVariant productId="O5D100">
                              <Name textId="T_NAME"/>
                              <Description textId="T_DESC"/>
                            </DeviceVariant>
                          </DeviceVariantCollection>
                        </DeviceIdentity>
                      </ProfileBody>
                      <ExternalTextCollection>
                        <PrimaryLanguage>
                          <Text id="T_NAME" value="O5D100 Optik Mesafe Sensörü"/>
                          <Text id="T_DESC" value="2m lazer sensör"/>
                        </PrimaryLanguage>
                      </ExternalTextCollection>
                    </IODevice>
                    """);
            }
            var iodd = LibraryImport.FromFile(ioddZip, "IODDfinder", "Üretici IODD");
            Assert.True(iodd.IsSuccess, iodd.Error?.Message);
            Assert.Equal("O5D100", iodd.Value[0].Definition.OrderCode);
            Assert.Contains("C/Q", iodd.Value[0].Definition.Functions[0].ConnectionPoints);

            // 3. AASX paketi (JSON payload ile)
            var aasxPath = Path.Combine(temp.FullName, "valve.aasx");
            using (var z = ZipFile.Open(aasxPath, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("aasx/aas-env.json");
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write("""
                    {
                      "submodels": [
                        {
                          "idShort": "Nameplate",
                          "submodelElements": [
                            { "idShort": "ManufacturerName", "value": "Festo" },
                            { "idShort": "OrderCode", "value": "MPA-S-VI" },
                            { "idShort": "ManufacturerProductDesignation", "value": "Valf Adası" },
                            { "idShort": "ETIMClass", "value": "EC001418" }
                          ]
                        }
                      ]
                    }
                    """);
            }
            var aasx = LibraryImport.FromFile(aasxPath, "Festo AASX", "Üretici AAS");
            Assert.True(aasx.IsSuccess, aasx.Error?.Message);
            Assert.Equal("Festo", aasx.Value[0].Definition.Manufacturer);
            Assert.Equal("MPA-S-VI", aasx.Value[0].Definition.OrderCode);

            // 4. EDZ paketi (part.xml)
            var edzPath = Path.Combine(temp.FullName, "breaker.edz");
            using (var z = ZipFile.Open(edzPath, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("items/part/ABB/part.xml");
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write("""
                    <parts>
                      <part partnr="ABB.2CDS253001R0164" P_ARTICLE_MANUFACTURER="ABB" P_ARTICLE_ORDERNR="S203-C16" P_ARTICLE_DESCR1="3 Kutup C16 MCB">
                        <functiontemplate functiondefname="Circuit breaker">
                          <connectionpoint designation="1"/>
                          <connectionpoint designation="2"/>
                          <connectionpoint designation="3"/>
                          <connectionpoint designation="4"/>
                        </functiontemplate>
                      </part>
                    </parts>
                    """);
            }
            var edz = LibraryImport.FromFile(edzPath, "ABB Portal", "Ticari kullanım");
            Assert.True(edz.IsSuccess, edz.Error?.Message);
            Assert.Equal("S203-C16", edz.Value[0].Definition.OrderCode);
            Assert.Equal(FunctionKind.Protection, edz.Value[0].Definition.Functions[0].Kind);

            // 5. SkyCAD XML
            var skyPath = Path.Combine(temp.FullName, "relay.skycad");
            File.WriteAllText(skyPath, """
                <SkyCAD>
                  <CataloguePart>
                    <Manufacturer>Finder</Manufacturer>
                    <PartNumber>40.52.9.024.0000</PartNumber>
                    <ClassName>Relay</ClassName>
                    <Description>24VDC 2CO Röle</Description>
                    <Terminal Name="A1"/>
                    <Terminal Name="A2"/>
                    <Terminal Name="11"/>
                    <Terminal Name="14"/>
                  </CataloguePart>
                </SkyCAD>
                """, Encoding.UTF8);
            var sky = LibraryImport.FromFile(skyPath, "SkyCAD Kataloğu", "Açık paylaşım");
            Assert.True(sky.IsSuccess, sky.Error?.Message);
            Assert.Equal("40.52.9.024.0000", sky.Value[0].Definition.OrderCode);
            Assert.Equal(["A1", "A2", "11", "14"], sky.Value[0].Definition.Functions[0].ConnectionPoints);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
