# ADR-0056: Hazır ürün kütüphanesi, masaüstü dışa aktarma / etiket PDF'i ve PLC adresi ↔ tel çapraz referansı

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-20
- **İlişki:** ADR-0007 (komponent kütüphanesi), ADR-0035 (standart sembol seti), ADR-0043 (kablo etiketleri), ADR-0054 (bileşen özellikleri), ADR-0055 (yeni sayfa ve model formları), ROADMAP Faz 6b-4, 6c-3, 6c-4

## Bağlam
Yaygın endüstriyel E-CAD iş akışlarıyla yapılan karşılaştırmada üç ana eksik belirlendi:
1. Varsayılan kütüphanede hazır semboller olduğu hâlde standart endüstriyel ürün kayıtları (`StandardProducts`), masaüstünden ürün kataloğu içe aktarma / ürün seçerek cihaza atama (`DevicePropertiesWindow > Ürün` sekmesi) ya da üründen cihaz oluşturma ekranı yoktu (Faz 6b-4).
2. Proje PDF'i, DXF/DWG, Excel/CSV mühendislik raporları ve baskıya hazır kablo/tel etiketi PDF'i yalnızca CLI üzerinden üretilebiliyor, masaüstü arayüzünde dışa aktarma merkezi bulunmuyordu (Faz 6c-3).
3. PLC adresi ile tel numarası arasında iki yönlü sıralanabilen çapraz referans tablosu, kanal atama düzenleyicisi ve CSV PLC adres listesi içe aktarma arayüzü yoktu (Faz 6c-4).

## Karar
- **Hazır ürün kataloğu (`Ake.Library.StandardProducts`):**
  - 9 kategoride (güç kaynağı, kontaktör/röle, koruma, M8/M12 sensör, kumanda/sinyal, motor/sürücü, haberleşme, PLC/GÇ, klemens/pnömatik) 32 standart ürün tanımı; her biri kaynak (`Source`) ve lisans (`License`) alanı taşır. `StandardSymbols.EnsureInstalled` açılışta varsayılan kütüphaneye bunları da yükler.
  - Editörde `ProductSelectionWindow` (`Ekle > Ürün kütüphanesi…` ve `DevicePropertiesWindow > Ürün > Kütüphaneden ürün seç…`): kategorili arama, JSON/CSV ürün içe aktarma, hızlı ürün ekleme ve üründen otomatik tag'li cihaz üretimi (`CreateDeviceFromLibraryProduct`).
- **Masaüstü dışa aktarma ve baskıya hazır etiket PDF'i (`Ake.Render.LabelPdfExporter`, `ExportWindow`):**
  - `LabelPdfExporter`, `ReportCatalog.Labels` verisini kesim kılavuz çizgili ve çift uç hedefli A4 etiket tabakaları (3 × 10 düzeni) olarak PDF'e yazar.
  - `ExportWindow` (`Başlangıç > Dışa aktar / Raporlar…`, `Ctrl+P`, Dosya menüsü): çok sayfalı proje PDF'i, sayfa/proje DXF/DWG, etiket PDF'i ve önizlemeli tüm raporların Excel (`.xlsx`) / CSV (`.csv`) çıktısını tek merkezden sunar.
- **PLC adresi ↔ tel numarası çapraz referansı (`ReportCatalog.PlcCrossReference`, `PlcIoWindow`):**
  - `ReportCatalog.PlcCrossReference(project, sortByWire)` ve oturum tarafında `PlcCrossReferenceRows` / `AssignPlcChannel` / `ImportPlcAddressesCsv`: her PLC kanalını bağlı olduğu potansiyeldeki tel/kablo damarı, klemens ve saha elemanı ucuyla birleştirir; PLC adresine veya tel numarasına göre sıralar ve CSV/noktalı virgüllü adres listelerini tek geri alma adımında kanallara yazar.
- **Genişletilmiş standart sembol seti ve yerel sembol koleksiyonları (`StandardSymbols.Extended`, `StandardSymbols.Collections`, `SymbolSelectionWindow`):**
  - `StandardSymbols.Extended.cs` ile mevcut 9 elektrik/otomasyon kategorisine 62 yeni IEC 60617 sembolü eklendi (14 pnömatikle toplam 117 dahili standart sembol).
  - `StandardSymbols.Collections.cs` (`DiscoverCollections`, `ImportFromPath`, `ImportDiscoveredCollections`), çalışma alanındaki QElectroTech (`.elmt`), B. Favre DXF (`fav0001_dxf_library` — çerçeve/antet hariç) ve `.sembol.json` kütüphanelerini keşfedip lisanslarıyla içe aktarır; `SymbolSelectionWindow` içindeki *"Dosyadan al (.elmt / .dxf)…"* , *"Klasörden al…"* ve *"Bulunan kütüphaneleri yükle"* butonları ile editör açılışında (`App.axaml.cs`) bu koleksiyonları yükler. `SymbolPalette.FriendlyFolder`, Favre ve QElectroTech numaralı klasörlerini Türkçe gruplara ayırır.

## Doğrulama
`StandardSymbolsTests`, `SymbolPaletteTests`, `ReportTests`, `AutoPageTests` ve `ProjectInfoTests` birim testleri; `dotnet format --verify-no-changes` ve `Ake.EditorSnapshot` ekran görüntüleriyle görsel denetim.
