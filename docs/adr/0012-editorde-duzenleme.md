# ADR-0012: Editörde düzenleme: seçim, taşıma, döndürme, geri alma, kaydetme (Faz 6, ikinci dilim)

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16

## Bağlam

ADR-0010'daki editör yalnızca görüntüleyiciydi. Bu dilim ilk gerçek düzenlemeyi ekler: bir sembolü
sayfada taşımak ve sonucu kaydetmek. Yol haritası şartı: "Taşıma / döndürme yalnızca `Placement`
değiştirir (bağlantı korunur — test)".

## Kararlar

| Konu | Karar |
|---|---|
| Her düzenleme bir Command'dır | Editör modeli doğrudan değiştirmez. Taşıma ve döndürme `MovePlacement` command'ıdır; CLI ve patch ile aynı yoldan geçer, geri alınır ve audit'e yazılır (ADR-0001). |
| `MovePlacement` | Yerleşimin konumunu ve dönüşünü değiştirir; elektriksel modele dokunmaz. Konum sonlu, dönüş 90'ın katı olmalıdır. |
| Oturum karar verir, pencere gösterir | Seçim (isabet testi), yapışma, taşıma, döndürme, geri alma ve kaydetme `EditorSession` içindedir ve pencere açmadan test edilir. `SceneView` yalnızca basılan noktayı ve sürükleme mesafesini milimetre olarak bildirir. |
| İsabet testi | Çizim katmanı her yerleşimin sayfadaki kutusunu verir (`SheetRenderer.SymbolBoxes`). Tıklanan noktayı içeren kutu seçilir; üst üste gelenlerde en küçüğü. 1 mm tolerans vardır. |
| Yapışma | Sürükleme mesafesi 5 mm'ye yuvarlanır. Yuvarlanan mesafe sıfırsa command çalışmaz ve geçmişe kayıt düşmez. Konumun kendisi yuvarlanmaz; adım dışında duran bir sembol adım dışında kalır. |
| Sürükleme önizlemesi | Sürüklerken model değişmez; bırakılacak yer kesikli bir kutuyla gösterilir. Bırakınca tek bir command çalışır. Böylece bir sürükleme, geçmişte tek adımdır. |
| Görünüm korunur | Düzenlemeden sonra sayfa yeniden çizilir ama yakınlaştırma ve kaydırma değişmez. Yeniden sığdırma yalnızca sayfa değişince yapılır. |
| Teller kendiliğinden izler | Tel yolu modelde saklanmadığı için (ADR-0011) taşınan sembolün telleri ve çapraz referansları yeniden hesaplanır. |
| Kaydetme | Proje açıldığı dosyaya yazılır; son kaydetmeden beri yapılan her işlem (geri alma dahil) audit log'a eklenir. Değişiklik yoksa audit dosyası oluşturulmaz. |
| Kaydedilmemiş değişiklik | Durum satırında "kaydedilmedi" yazar. Ölçüt audit kaydıdır: bir değişikliği yapıp geri almak da kaydedilecek bir şey bırakır. |
| Kısayollar | Ctrl+Z geri al, Ctrl+Y yinele, Ctrl+S kaydet, R seçili sembolü döndür, Esc seçimi bırak. Yol kutusuna yazarken devreye girmezler. |

## Doğrulama

Editör gerçek pencerede denendi: referans makinenin geçici bir kopyası açıldı, motor sembolü fareyle
sürüklendi, bırakıldı ve Ctrl+S ile kaydedildi. Sürükleme sırasında seçim vurgusu ve kesikli hedef
kutusu göründü; bırakınca sembol yeni yerine oturdu ve çapraz referansları onunla taşındı; dosyadaki
konum ve audit kaydı değişti.

## Yapılmayanlar

- Yeni sembol yerleştirme (kütüphaneden sürükle-bırak), silme, tel çekme, özellik düzenleme.
- Çoklu seçim, kopyala/yapıştır, hizalama.
- Kaydedilmemiş değişiklikle pencereyi kapatırken uyarı. **Şu an pencere kapanınca kaydedilmemiş
  değişiklikler sessizce kaybolur.**
- "Farklı kaydet" ve yeni proje.
- Navigator'lar ve ERC bulgu listesi.

## Bilinen eksikler

- **Döndürme (R) ve geri alma (Ctrl+Z) gerçek pencerede gözle doğrulanmadı**; oturum testleriyle
  doğrulandı.
- **Sürüklerken teller izlemez**; yalnızca kutu hareket eder, teller bırakınca yeniden çizilir.
- **Taşınan sembol başka bir sembolün üzerine bırakılabilir**; çakışma denetimi yok.
- **Sayfa dışına taşıma engellenmez.**
- **Her düzenlemeden sonra bütün sayfa ve ERC yeniden hesaplanır.** Referans makinede fark edilmiyor;
  büyük projede ölçülmedi.
