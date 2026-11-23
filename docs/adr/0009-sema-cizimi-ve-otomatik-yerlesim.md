# ADR-0009: Şema çizimi ve otomatik yerleşim (Faz 5, ikinci kısım)

- **Durum:** Kabul edildi; otomatik yerleşim "sürüm 0"dır. Tel yolu ve aşağıdaki kusurların dördü
  [ADR-0011](0011-tel-yolu.md) ile değişti.
- **Tarih:** 2026-11-15

## Bağlam

ADR-0008 sayfanın çerçevesini ve antetini çizdi; çizim alanı boştu. Bu ADR sembolleri, telleri ve
sayfalar arası referansları ekler. Yapay zeka özelliği (Faz 9) elektriksel modeli üretecek, çizimi
ise bu katman yapacak; bu yüzden buradaki kalite o özelliğin de sınırıdır.

## Kararlar

### Semboller

| Konu | Karar |
|---|---|
| Parametrik üretim | Semboller elle çizilmez; function'ın türünden ve bağlantı noktası sayısından kurala göre üretilir (`SymbolLibrary`). Faz 4'ten ertelenen "parametrik sembol üretici" budur. |
| Kapsam | NO ve güç kontağı, NC kontak, sigorta/koruma, bobin, klemens, 3 fazlı motor. Diğer her şey (güç kaynağı, PLC kanalı, genel cihaz) uçları üste ve alta dağıtılmış bir kutudur. |
| Başlangıç noktası | Sembolün ilk bağlantı ucudur. İki uçlu semboller 15 mm boyundadır; çok kutuplu cihazlarda kutup adımı 10 mm'dir. Motorun faz uçları da aynı adımdadır, böylece üstündeki kontaktörle hizalanır. |
| Uç yönü | Her uç, telin sembolden uzaklaştığı yönü taşır. Tel yolu ve çapraz referans bunu kullanır. |
| Biçim | IEC 60617'nin sadeleştirilmiş halidir; standartla birebir karşılaştırılmadı. |

### Sayfa içeriği

| Konu | Karar |
|---|---|
| Yerleşim koordinatı | `Placement.X/Y`, çizim alanının sol üst köşesine göre milimetredir; şablonun kenar boşluğu değişse de yerleşim kaymaz. |
| Dönüş | 0, 90, 180, 270 derece. Metinler dönmez, yalnızca konumları döner. |
| Tag | Sembolün solunda, ürün tag'i olarak (`-K1`). Aynı cihazın yan yana duran sembollerinde yalnızca en soldakine yazılır. |
| Tel | İki ucu da aynı sayfada olan bağlantı, üç parçalı dik yolla çizilir; tel numarası en uzun parçanın üzerine yazılır. |
| Tel rengi | Tel renk kodundan (BU, GNYE, RD, OG…) çizgi rengine çevrilir; bilinmeyen kod siyahtır. |
| Çapraz referans | Öbür ucu başka sayfada olan bağlantı kısa bir uzantı ve `ad / sayfa.kolon` notuyla biter. Ad, potansiyelin adıdır; adsızsa karşı ucun kısa tag'idir (`-K1:14`). Not telin uzantısında dik yazılır. |
| Yerleşmemiş karşı uç | Hiçbir sayfada olmayan uca giden bağlantı çizilmez. |

### Otomatik yerleşim (sürüm 0)

| Konu | Karar |
|---|---|
| Kapsam | Ana görünümü olmayan her function, fonksiyon düğümü sayfanınkiyle aynı olan sayfaya yerleşir. Sayfası ya da şablonu olmayan function yerleşmez. |
| Yöntem | Modeli değiştirmez; `AddPlacement` command'larından oluşan tek bir transaction üretir (geri alınabilir, audit'e yazılır). |
| Satırlar | Cihazlar faz beslemesine uzaklıklarına göre satırlara dizilir: besleme üstte, yük altta. PE ve nötr üzerindeki bağlantılar uzaklık hesabına katılmaz. |
| Kolonlar | Bir cihazın kutupları önce ve sabit adımla dizilir; alt alta gelen cihazların kutupları aynı kolona düşer ve teller düz iner. |
| Yeni command'lar | `AddPage`, `AddPlacement`; patch'e `addPage`. |

### CLI

```text
ake layout <proje.json> [--dry-run]
```

## Bilinen kusurlar

Bunlar görsel doğrulamada görüldü; "yapılmadı" değil, "yanlış çiziliyor"dur.

- **Tel yolu engel tanımaz.** Tel başka bir sembolün içinden geçebilir ve üst üste binen iki tel
  tek tel gibi görünür. Referans makinenin 1. sayfasında PE teli nötr klemensinin üzerinden geçiyor
  ve klemens köprüsü aradaki klemenslere bağlıymış gibi duruyor. **Çıktı bu haliyle üretim belgesi
  olarak kullanılamaz.**
- **Bağlantı noktası (düğüm) işareti yok.** Üç telin birleştiği yerde nokta çizilmez.
- **Kalabalık sayfalarda notlar üst üste biner.** Aynı uçtan çıkan çok sayıda sayfa dışı bağlantı
  (24 V dağıtımı) okunmaz hale gelir.
- **PLC kartı kanal başına ayrı kutu olarak çizilir**; tek kart olarak değil.
- **Sayfa taşması denetlenmez.** Satır sayısı çizim alanını aşarsa semboller antetin üzerine iner.
- **Yerleşim devre tipini tanımaz.** Motor çıkışı gibi düzenli devrelerde okunaklıdır; kontrol
  devresinde değildir.

## Yapılmayanlar (Faz 5'in kalanı)

- Engelden kaçan tel yolu ve düğüm noktaları.
- Devre tipi bazlı yerleşim şablonları.
- Sayfa bazlı tag şeması (`3F3`) ve bobin altında kontak ayna tablosu.
- DXF/DWG çıktısı, PDF içindekiler ve yer imleri.
