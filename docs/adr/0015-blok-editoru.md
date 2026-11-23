# ADR-0015: Blok editörü

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0013'teki sembol tanımının ekranı.

## Bağlam

Proje sahibi "mutlaka bir blok editörü" istedi: hazır kütüphanede olmayan bir sembolü kullanıcı
kendisi çizebilmeli. Sembolün modeli, doğrulaması, dosya biçimi ve çizimi ADR-0013'te hazırdı; bu ADR
sembolü çizen ekranı kapsar.

## Kararlar

| Konu | Karar |
|---|---|
| Açılış | Ana pencerede Sembol > Yeni sembol, Sembol > Seçili sembolü düzenle (Ctrl+E) ve araç çubuğundaki "Blok editörü". Ayrı, kalıcı bir pencere açılır. |
| Başlangıç | Seçili sembolün function'ına kullanıcı sembolü atanmışsa o sembol açılır (aynı kimlik; kaydetmek onu günceller). Yoksa yerleşik parametrik sembolün **kopyası** açılır (yeni kimlik); kullanıcı sıfırdan değil, mevcut biçimden başlar. Seçim yoksa boş taslak. |
| Araçlar | Seç (S), Çizgi (L), Dikdörtgen (D), Daire (C, merkezden), Yazı (T), Uç (U). Çizim araçları bas-sürükle-bırak ile çalışır; sürüklerken hedef kesikli kutuyla gösterilir. |
| Izgara | 2,5 mm. Bütün noktalar, daire yarıçapı dahil, bu adıma yapışır. Uçlar ızgarada durduğu için sayfadaki 5 mm ızgarayla hizalanır. |
| Uçlar | Uç aracı, seçili yönde (yukarı, aşağı, sola, sağa) bir uç ekler. Uçlar sırayla numaralanır; numara function'ın bağlantı noktası sırasıdır. Aynı noktaya ikinci uç konamaz. "Ucu çevir" yönü 90 derece döndürür; "İlk uç yap" seçili ucu 1 numara yapar. İlk uç dolu nokta ile gösterilir. |
| Başlangıç noktası | Kaydederken bütün koordinatlar ilk uca göre kaydırılır: ilk uç (0, 0) olur. Yerleşim ve tel yolu sembolün başlangıç noktasını ilk uç kabul eder. Ekrandaki kırmızı artı, çalışma alanının merkezidir. |
| Seçim | Seç aracıyla tıklanan öğe seçilir; önce uçlar, sonra en son çizilen öğe. Seçili öğe sürüklenerek taşınır, Sil ile silinir. |
| Geri alma | Taslağın anlık görüntüsüyle (Ctrl+Z / Ctrl+Y). Taslak, kaydedilene kadar modelin parçası değildir; bu yüzden command değil. Kaydetme bir command'dır ve ana pencerede geri alınır. |
| Projeye kaydetme | Taslak doğrulanır (ADR-0013 kuralları) ve `PutSymbol` ile projeye konur. "Seçili function'a ata" işaretliyse `PutSymbol` + `AssignSymbol` tek bir transaction'dır: uç sayısı function'a uymazsa ikisi de yapılmaz. |
| Dosyaya kaydetme | `*.sembol.json` (ADR-0013 biçimi); başka projelere `ake symbol set` ile alınır. |
| Ayrım | Taslak mantığı `SymbolDraft` içindedir ve pencere açmadan test edilir; pencere yalnızca gösterir ve çağırır. |

### Başsız görüntü aracı

`tools/Ake.EditorSnapshot` editör pencerelerini **ekrana açmadan** (Avalonia başsız çizim) PNG'ye
çevirir: ana pencere, blok editörü (ilk sayfanın ilk sembolüyle) ve boş blok editörü. Arayüz
değişikliklerini görsel olarak denetlemek için kullanılır. Önceden bunun için gerçek pencere açılıp
masaüstünde fare ve klavye kullanılıyordu; bir denemede pencere öne gelmedi ve tıklama kullanıcının
terminaline gitti. Paketler: Avalonia.Headless ve Avalonia.Skia (MIT).

## Yapılmayanlar

- Yay, çoklu çizgi ve dolgulu şekil. QElectroTech ve DXF içe aktarıcılarıyla birlikte eklenecek.
- Öğeleri döndürme, aynalama, kopyalama, çoklu seçim.
- Yazı boyu ve hizası, çizgi kalınlığı seçimi (şimdilik sabit: 2,5 mm yazı, 0,25 mm çizgi).
- Sembol kütüphanesi görünümü (projedeki sembolleri listeleme, silme).
- Kaydedilmemiş taslakla pencereyi kapatırken uyarı.

## Doğrulama

Taslak davranışı birim testleriyle; pencerenin görünüşü başsız görüntü aracıyla doğrulandı (bobin
sembolüyle açılan blok editörü ve E-CAD düzenindeki ana pencere). Fareyle çizim gerçek pencerede
denenmedi.
