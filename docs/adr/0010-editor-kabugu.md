# ADR-0010: Editör kabuğu ve sayfa görüntüleyici (Faz 6, birinci dilim)

- **Durum:** Kabul edildi; arayüz ilkeleri [ADR-0014](0014-ecad-duzeni.md) ile değişti
- **Tarih:** 2026-11-15

## Bağlam

Masaüstü uygulaması zorunludur (ROADMAP, ek gereksinimler). Faz 6'nın tamamı (navigator'lar, dock
panelleri, sürükle-bırak, tel çekme) büyüktür; bu ADR ilk çalışan dilimi kapsar: bir projeyi açıp
sayfalarını ekranda görmek. Düzenleme henüz yoktur.

Proje sahibinin başka projelerde koyduğu arayüz ilkeleri burada da uygulanır: yazı tipi Lora
(sistem varsayılanı kullanılmaz), tek satırlık öğeler, açıklama metni yok, açılır pencere yok.
Yol haritasındaki "EPLAN benzeri dock'lanabilir paneller" bu ilkelerle kısmen çelişir; çelişki bu
dilimde ortaya çıkmadı ve navigator'lar eklenirken proje sahibine sorulacaktır.

## Kararlar

| Konu | Karar |
|---|---|
| Ekran, çıktıyla aynı sahneyi çizer | `SceneView`, PDF/SVG/PNG'nin kullandığı `Scene`'i Avalonia çizim bağlamına çizer. Ekrana özel ayrı bir çizim mantığı yoktur; ekranda görünen çıktıda çıkandır. |
| Oturum arayüzden ayrıdır | `EditorSession` açık projeyi, sayfa listesini, durum satırını ve sayfa sahnesini verir; Avalonia'ya bağlı değildir ve pencere açmadan test edilir. Pencere yalnızca gösterir. |
| Görünüm dönüşümü | `ViewTransform` (ölçek + kaydırma) saf bir değerdir: sığdırma, imleç altındaki noktayı sabit tutan yakınlaştırma, kaydırma. |
| Proje açma | Komut satırındaki ilk argüman ya da üstteki yol kutusuna yazıp Enter. Dosya seçme penceresi yoktur. Açma başarısız olursa neden durum satırında görünür ve açık proje kalır. |
| Yerleşim | Üstte yol kutusu, solda sayfa listesi, ortada çizim alanı, altta tek satır durum (proje adı, sayfa ve cihaz sayısı, ERC özeti, imleç konumu mm). |
| Yazı tipi | Lora, uygulamaya gömülüdür (`Assets/Fonts`, SIL OFL 1.1; lisans dosyası yanında). Çizimdeki yazılar sayfa şablonunun yazı tipini kullanır, arayüzünkini değil. |
| Etkileşim | Tekerlek yakınlaştırır, sürükleme kaydırır, pencere boyutu değişince sayfa yeniden sığdırılır. |

## Yapılmayanlar

- Düzenleme: sembol yerleştirme, taşıma, tel çekme, özellik düzenleme, undo/redo, kaydetme.
- Navigator'lar (cihaz, potansiyel, klemens, PLC, kablo), ERC bulgu listesi ve "bulguya git".
- Seçim, ızgara, yakalama (snap), kısayol profili.
- Sayfa sanallaştırma ve 300 sayfalık proje performans ölçümü.
- Antet logosu ekranda çizilmez (çıktıda çizilir).

## Bilinen eksikler

- **Yakınlaştırma ve kaydırma elle denenmedi.** Pencere açılıp ekran görüntüsü alınarak doğrulandı;
  fare etkileşimi yalnızca dönüşüm hesabının birim testleriyle doğrulandı.
- **Bellek:** referans makine açıkken yaklaşık 190 MB (Debug derlemesi). ADR-0001 hedefi 300 MB;
  Release ve kırpılmış yayında ölçülmedi.
- **Yol kutusu tek açma yoludur**; sürükle-bırak yoktur.
