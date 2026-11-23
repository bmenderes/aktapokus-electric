# ADR-0014: Editörün E-CAD düzeni: menü, navigator'lar, mesaj paneli, özellik penceresi

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0010'daki "açılır pencere yok, tek katman" ilkesinin yerine geçer.

## Bağlam

ADR-0001 arayüz için "yaygın E-CAD kullanım alışkanlığı" diyordu; ADR-0010 ise proje sahibinin başka
projelerdeki sade ilkeleri (açılır pencere yok, tek katman, tek satır) uygulamıştı. Proje sahibi
ikisinin çeliştiği sorulunca **E-CAD düzenini** seçti: amaç, bu araçlara alışmış mühendislerin
alışkanlığını değiştirmemek. Yazı tipi tercihi (Lora; sistem varsayılanı kullanılmaz) geçerliliğini korur.

## Kararlar

| Konu | Karar |
|---|---|
| Ana pencere | Üstte menü (Proje, Düzen, Görünüm, Sayfa) ve araç çubuğu; solda navigator'lar; ortada çizim alanı; altta mesaj paneli; en altta durum satırı. Bölmeler arasındaki sınırlar sürüklenerek boyutlanır. |
| Navigator'lar | Sekmeli: **Sayfalar**, **Cihazlar** (tam tag sırasıyla; hangi sayfada olduğu ya da "yerleştirilmemiş"), **Potansiyeller** (adı olanlar ve nokta sayısı). Bir satıra çift tıklayınca sayfası açılır, sembol seçilip ortalanır. |
| Mesaj paneli | ERC bulguları, önce hatalar. Çift tıklayınca bulgunun nesnesine gidilir ("bulguya git"). Başlıkta bulgu sayısı ya da "ERC temiz". Her düzenlemeden sonra yenilenir. |
| Özellik penceresi | Sembole çift tıklayınca (ya da Enter, menü, araç çubuğu) açılan kalıcı pencere: tam tag, ürün tag'i (düzenlenir), açıklama, ürün, tip, function'lar. Tamam tek bir `Retag` command'ı çalıştırır; geri alınır. |
| Proje açma | Proje > Aç (Ctrl+O) dosya seçme penceresi açar. Komut satırı argümanı da çalışır. Önceki yol kutusu kaldırıldı. |
| Kısayollar | Ctrl+O aç, Ctrl+S kaydet, Ctrl+Z / Ctrl+Y geri al / yinele, R döndür, Enter özellikler, Home sığdır, PageUp / PageDown önceki / sonraki sayfa, Esc seçimi bırak. Menüde gösterilir. |
| Komut durumu | Geri al, yinele, döndür ve özellikler, yapılamadıklarında menüde ve araç çubuğunda soluk görünür. |
| Izgara | Görünüm > Izgara ile açılıp kapanır. |
| Oturum karar verir | Navigator satırları, "bulguya git" konumu (`Locate`), özellikler ve yeniden adlandırma `EditorSession` içindedir ve pencere açmadan test edilir. |

## Yapılmayanlar

- **Panellerin taşınıp pencereden ayrılması (dock).** Paneller şimdilik sabit yerde, yalnızca boyutlanır.
  Taşınabilir paneller için Avalonia'nın Dock kütüphanesi (MIT) değerlendirilecek; Avalonia 12 ile
  uyumluluğu denenmedi.
- **Klemens, PLC ve kablo navigator'ları.** Yol haritasında var; veri hazır, ekran yazılmadı.
- **Navigator'dan sayfaya sürükle-bırak**, yapı ağacı (=/+) görünümü, filtre.
- **Özellik penceresinde** yalnızca ürün tag'i düzenlenir. Açıklama, ürün, yapı düğümü ve bağlantı noktası
  adları düzenlenmez (bunlar için command'lar yazılacak).
- **Kısayol profili** (yapılandırılabilir tuş eşlemi).
- **Kaydedilmemiş değişiklikle kapatırken uyarı** hâlâ yok (ADR-0012).

## Doğrulama

Pencere, kısa devreli bir referans makine kopyasıyla açılıp **fare ve klavye kullanılmadan**
görüntülendi: menü, araç çubuğu, navigator sekmeleri, ızgaralı çizim alanı ve mesaj panelindeki kısa
devre bulgusu doğru göründü. Çift tıklama, sekme geçişi, "bulguya git" ve özellik penceresi gerçek
pencerede denenmedi; oturum testleriyle doğrulandı. Bellek, açık referans projeyle yaklaşık 210 MB
(Debug).
