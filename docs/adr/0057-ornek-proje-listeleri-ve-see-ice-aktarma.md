# ADR-0057: Örnek Proje Listeleri, DXF/DWG Sayfa İçe Aktarma ve SEE Electrical Çıktıları

- Durum: Kabul edildi
- Tarih: 2026-11-21

## Bağlam

Örnek referans projesindeki Excel mühendislik listeleri (motor/redüktör, ada, kontrolör G/Ç ve sürücü G/Ç listeleri), PacDrive çoklu görünüm DXF çizimleri ve SEE Electrical'dan dışa aktarılan tam DXF/DWG çizimleri ile liste çıktılarının editör ve CLI üzerinden tek akışla içe aktarılması ile dışa aktarılabilmesi gerekti.

## Karar

1. **DXF arşiv (.dxf.zip) ve sayfa içi serbest grafik aktarımı (`Ake.Library`)**:
   - `DxfImport.ReadDocument` ile hem tekil `.dxf`/`.dwg` hem de `.dxf.zip` arşivleri açılır; arşiv içindeki tekil CAD dosyaları sembol kırpma veya incelemeye alınabilir.
   - `DxfPageImport.Read` ve `DxfPageImport.Plan`, tam bir CAD çizimindeki (veya klasördeki tüm çizimlerdeki) varlıkları ACI renkleri, döndürülmüş yazılar, yaylar/daireler ve çerçeve ayıklamayla birlikte doğrudan `PageGraphic` varlıklarına dönüştürerek geri alınabilir tek bir `Transaction` üretir (`ake import dxf` ve editör `proje.dxf_al`).

2. **SEE Electrical mühendislik listelerinin modele aktarımı (`SeeListImport`)**:
   - `ClosedXML` kullanılarak `.xlsx` ve UTF-8 `.csv` çalışma sayfaları taranır.
   - Yan yana dört sütunlu bloklar halinde tutulan kontrolör I/O (`Klemens | Nr | İsim | Kod`) ve sürücü I/O (`Nr | İsim | Kod | Klemens`), motor/redüktör ve valf adası/sensör listeleri tek bir geri alınabilir `Transaction` ile `StructureNode`, `Device`, `SetDeviceDetails`, `SetDeviceProperty`, `Terminal` ve `Controller` öğelerine dönüştürülür (`ake import list` ve `proje.liste_al`).

3. **Dört özel rapor şablonu (`ReportCatalog.SpecialNames`)**:
   - Mevcut kararlı rapor kümesi (`ReportCatalog.Names`) değiştirilmeden, `motors`, `islands`, `controller-io-blocks` ve `driver-io-blocks` rapor düzenleri `ReportCatalog.SpecialNames` altına eklendi. Excel ve metin dışa aktarım penceresi bu şablonları doğrudan sunar.

4. **Kalıcı haberleşme ve tek hat şeması sayfaları (`CliApp`)**:
   - `ake haberlesme` ve `ake tekhat` komutlarında `--sayfa-ekle` bayrağı ve isteğe bağlı `--cikti` desteklenerek `PageKind.CommunicationDiagram` ve `PageKind.SingleLineDiagram` sayfalarının doğrudan proje JSON'una kaydedilmesi sağlandı.

## Sonuçlar

- Örnek proje yapısındaki Excel tabloları kayıpsız içe ve dışa aktarılabilir.
- `examples/dxf/pacdrive-lxm62.dxf` üzerinden PacDrive çoklu görünüm sembol kırpma ve sayfa grafik aktarımı deterministik olarak test edilebilir.
