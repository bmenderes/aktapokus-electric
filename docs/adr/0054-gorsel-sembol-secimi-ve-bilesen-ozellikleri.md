# ADR-0054: Görsel sembol seçimi, bileşen özellikleri, sağ tık kolaylıkları ve kapak düzeni

Durum: kabul edildi (2026-11-20)

## Bağlam
Şema düzenlerken sağ tık menüsünden doğrudan sembol ekleyebilmek, eklenen sembolü görsel bir ızgaradan seçip ardından etiket, bağlantı noktası ve ürün bilgilerini tek pencerede düzenleyebilmek standart bir E-CAD iş akışıdır. Ek olarak, kapak sayfasındaki proje bilgilerinin sayfanın tam ortasında dar bir sütuna sıkışması yerine sayfaya dengeli yayılması ve nokta ızgarasının yalnızca şema sayfalarında çizilmesi gerekti.

## Karar
- **Kapak sayfası düzeni (`SheetRenderer.AutoPages`):** Üstte tam genişlikte çerçeveli başlık kutusu (üst şeritte sayfa başlığı, ortada proje adı ve açıklaması), ortada dolu proje alanlarını geniş sayfalarda iki sütunlu kartlara dağıtan bilgi alanı (`Firma adresi` dahil; boş alanlar çizilmez), altta toplam sayfa, şablon ve standart özet şeridi yer alır.
- **Nokta ızgarası:** `EditorSession.IsSchematicPage` ile nokta ızgarası yalnızca `PageKind.Schematic` sayfalarında çizilir; kapak ve tablo sayfaları temiz kalır.
- **Sağ tık kolaylıkları:**
  - Boş şema alanında sağ tıkta *Sembol ekle…* (`Insert`), *Tel çek* (`W`), *Metin ekle…* ve *Makro ekle* (`Ctrl+M`) doğrudan el altındadır.
  - Sembol üzerinde sağ tıkta *Özellikler* (`Enter`), *Sembol değiştir…*, *Tel çek* (`W`), *Döndür*, *Kopyala*, *Kes*, *Sil*, *Sembolü düzenle* yer alır.
  - Boş alandan *Sembol ekle…* çalıştırıldığında kütüphane açılarak görsel seçim penceresi sunulur; seçilen sembol tıklanan noktaya konur ve ardından bileşen özellikleri penceresi otomatik açılır.
- **Görsel sembol seçimi (`SymbolSelectionWindow`):** Sol tarafta süzgeç, `Ağaç`/`Liste` kategori gezgini ve seçili sembol bilgi kutusu; sağ tarafta küçük `SceneView` önizlemelerinden oluşan ızgara ve yön/varyant seçimi (`0°`, `90°`, `180°`, `270°`) bulunur.
- **Gelişmiş bileşen özellikleri (`DevicePropertiesWindow`):** Beş sekmeli yapıya yükseltilmiştir:
  1. Birincil fonksiyon sekmesi (*Bobin*, *NO kontak*, *Genel cihaz* vb.): görünen etiket (`[...]` mevcut cihaz seçici ve `[×]` temizle), canlı tam etiket önizlemesi (`=Fonksiyon+Yer-Ürün`), `¶` ayraçlı hızlı uç adları/kodları, fonksiyon/cihaz açıklaması, *Ana fonksiyon* rolü (`SetPlacementRole`) ve kategori süzgeçli *Özellikler* tablosu.
  2. *Görüntüleme*: döndürme açısı, `X/Y` konumu ve görünüm rolü.
  3. *Sembol / Noktalar*: sembol önizlemesi, *Sembol seç…* düğmesi ve tüm uçların ayrıntılı tablosu.
  4. *Ürün*: üretici, sipariş kodu, tip.
  5. *Kontak aynası*: cihazın tüm fonksiyonlarının türü, uçları, rolü ve `sayfa.kolon` konumu.
  Alt kısımda *Tamam*, *İptal* yanında pencereyi kapatmadan uygulayan *Uygula* düğmesi yer alır.

## Bu dilimde olmayanlar
Alt bileşen özellik tablosunda kullanıcı tanımlı özel özellik satırı ekleme/silme ve kapak sayfasına serbest firma logosu görseli yerleştirme editör formu sonraki adımlara bırakılmıştır.
