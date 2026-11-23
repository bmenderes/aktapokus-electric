# ADR-0046: Sayfa türleri, kapak sayfası ve tablo sayfaları

Durum: kabul edildi (2026-11-19, proje sahibi: "standart elektrik devre şeması programlarında olan kapak sayfası gibi sayfalar bizde de olmalı"; dilim: sayfa türleri + kapak)

## Bağlam
Yaygın E-CAD programlarında "Yeni sayfa" penceresinde sayfa türü seçilir (şema, kapak, içindekiler, malzeme listesi, klemens planı, kablo listesi…). Bizde yalnızca şema, haberleşme şeması ve tek hat vardı; raporlar yalnızca CSV/XLSX'ti.

## Karar
- `PageKind` yeni türler: **Cover** (kapak), **Contents** (içindekiler), **PartsList** (malzeme listesi), **TerminalPlan** (klemens planı), **CableList** (kablo listesi). Bunlar gerçek proje sayfalarıdır: antetli, numaralı, PDF'e ve içindekilere girer, çerçeve ve antet sayfa şablonundan gelir.
- İçerik projeden üretilir ve proje değişince sayfa kendiliğinden güncellenir. Tablo sayfaları mevcut raporları (`ReportCatalog`) çizer; `Ake.Render`, `Ake.Reports`'a bağımlı olur (Core'a yeni bağımlılık yok).
- **Kapak:** proje adı, `description` özelliği (alt başlık), sayfa başlığı ve şu proje özellikleri (değeri olmayan satır çizilmez): `customer` Müşteri, `commission` Sipariş/iş no, `drawingNo` Çizim no, `company` Firma, `drawnBy` Çizen, `date` Tarih, `revision` Revizyon. Özellikler `ake apply` (`setProperty`) ile verilir; Proje özellikleri penceresi sonraki dilimdedir.
- **Devam sayfaları:** tablo bir sayfaya sığmazsa `Page.Part` (0, 1, 2…) ile devam sayfaları eklenir; sayfa kendi diliminin satırlarını gösterir. Editör, tablonun bugünkü satırına yetecek kadar sayfayı tek adımda ekler (numaralar 6, 6.1, 6.2; ADR-0042). Tablo sonradan büyür ve devam sayfası yoksa sayfa "devam sayfası ekleyin" uyarısı verir; fazla devam sayfası "satır kalmadı" uyarısı verir.
- **Dosya biçimi:** sayfada isteğe bağlı `kind` (zaten vardı) ve `part` alanı; varsayılan (şema, 0) yazılmaz, şema numarası artmaz.
- **Editör:** Sayfa sekmesi ve sağ tık menüsündeki "Yeni sayfa…" penceresi tür, başlık ve konum (listenin sonu / etkin sayfadan sonra alt numara) sorar. "Araya sayfa ekle (3.1)" hızlı şema sayfası olarak kalır.
- Marka adı hiçbir yerde kullanılmaz.

## Bu dilimde olmayanlar (Faz 6d, ROADMAP)
Potansiyel özeti, pin-bağlantı şeması, PLC şeması, revizyon özeti, çerçeve dokümantasyonu sayfa türleri; proje oluşturma ve proje özellikleri pencereleri; sayfa gezgininde özellik süzgeci ve grafik önizleme.
