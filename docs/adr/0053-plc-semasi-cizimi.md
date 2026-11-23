# ADR-0053: PLC şeması çizimi

Durum: kabul edildi (2026-11-19, proje sahibi: "PLC şeması"; Faz 6d, ADR-0050'deki tablo sayfasının yerine)

## Bağlam
ADR-0050 PLC şeması sayfasını G/Ç listesi tablosu olarak üretiyordu. Standart E-CAD programlarında PLC şeması, her modülü kutu olarak ve kanallarını satır satır çizen sayfadır.

## Karar
- `PageKind.PlcDiagram` artık tablo sayfası değil, **çizim sayfasıdır** (`SheetRenderer.IsTable` yanlış, `IsGenerated` doğru). Dosya biçimi değişmez; eski sayfalar yeni çizimle açılır.
- **Modül kutusu:** başlık satırı (modül tag'i, açıklaması, ürün sipariş kodu, `kontrolör › ağ › node`) ve altında kanal başına bir satır; modül kalın çerçeveli, sütunlar ince dikey çizgiyle ayrılır; modüller arasında bir satır boşluk.
- **Satırdaki sütunlar:** Kanal (DI0…), Adres, Sembolik ad, Açıklama, Modül ucu, Klemens, Saha ucu (kanalın bağlı olduğu uç). İçerik G/Ç raporundan (`ReportCatalog.Io`) gelir; bu yüzden rapor ile sayfa aynı veriyi gösterir.
- **Devam sayfaları:** sayfaya sığmayan modül/kanallar `Page.Part` ile sürer (ADR-0046); modül sayfa ortasında bölünürse yeni sayfa modülün başlığını "… devam" diye yeniden yazar. Bir modülün başlığı ile en az bir kanalı aynı sayfada kalır. Devam sayfası yoksa "devam sayfası ekleyin", fazla devam sayfası "satır kalmadı" uyarısı verir. Editör gereken sayfa sayısını kendisi ekler (`SheetRenderer.PartCount`).
- PLC modülü yoksa sayfada "(PLC modülü yok)" yazar.

## Bu dilimde olmayanlar
Kanalın saha cihazına çizgiyle bağlanması (şimdilik saha ucu metindir), modül başına sembol/ürün görseli, kanal adresinin otomatik atanması, bus topolojisi çizimi (bu haberleşme şemasıdır, ADR-0037), PLC adresi ↔ tel numarası çapraz referans tablosu (adres listesi bekleniyor).
