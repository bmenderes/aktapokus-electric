# ADR-0024: Taşınabilir paneller ve sekmeli sayfalar (Dock.Avalonia)

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0014'teki "paneller sabit yerde" eksiğini kapatır; ADR-0023'teki panel gizleme bu düzene taşındı.

## Bağlam

Yaygın E-CAD araçlarında navigator'lar ve mesaj paneli taşınabilir, birbirinin yanına sekme olarak
bırakılabilir, kenara iğnelenip otomatik gizlenebilir; birden çok sayfa sekmede açık durur. Proje sahibi
sırayı "önce taşınabilir paneller, sonra sürükle-bırak" olarak verdi.

## Kararlar

| Konu | Karar |
|---|---|
| Kütüphane | **Dock.Avalonia 12.1.5** (MIT; Dock.Model.Avalonia, Dock.Avalonia.Themes.Fluent ve bağımlılıkları MIT). `Dock.Serializer.SystemTextJson` kullanılmaz: kaynak üreticisi bizim derleyicimizden yeni bir derleyici istiyor ve uyarıyı hata sayıyoruz; `Dock.Model.Avalonia` içindeki serileştirici yeterli. |
| Düzen | `EditorLayout`: solda navigator'lar (Sayfalar, Cihazlar, Potansiyeller, Semboller; sekmeleri altta), ortada sayfa sekmeleri, altta Mesajlar. Paneller sürüklenip başka kenara ya da başka panelin yanına bırakılır. Kapatılamaz, pencere dışına çıkarılamaz (şimdilik). |
| Sayfa sekmeleri | Sayfa listesinden seçilen sayfa sekmede açılır, açıksa öne gelir. Her sekmenin kendi çizim alanı vardır; yakınlaştırma sekmeye özgüdür. Sekme kapatılabilir. Etkin sekme sayfa listesinde seçili görünür; Önceki/Sonraki sayfa, navigator'dan "git" ve ERC "bulguya git" sekme açar. Düzenlemeden sonra **bütün açık sekmeler** yenilenir (çapraz referans başka sayfayı da değiştirebilir). |
| Gizleme | Görünüm > Navigator / Mesajlar paneli **kenara iğneler** (otomatik gizler; kenardaki dik sekmeyle açılır). Tam çizim alanı (F11) ikisini iğneler ve şeridi daraltır. Dock'un iğneleyip geri açınca 0'da bıraktığı bölme oranı varsayılana döndürülür. |
| Kayıt | Düzen kapanışta `%LOCALAPPDATA%\aktapokus-electric\layout.json`'a yazılır, açılışta okunur. Sayfa sekmeleri kaydedilmez (projeye bağlıdır). Panel içerikleri dosyada yoktur; kimlikleriyle yeniden bağlanır. Bozuk ya da eksik panelli dosyada varsayılan düzen kalır ve durum satırında söylenir. Görünüm > **Varsayılan düzen** her zaman geri getirir. |
| Test edilebilirlik | Düzen dosyasının yolunu uygulama verir (`MainWindow.LayoutPath`); görüntü aracı ve testler kullanıcının düzenine dokunmaz. |

## Doğrulama

Başsız görüntü aracı: üç sayfa sekmede, tam çizim alanında kenardaki dik panel sekmeleri, düzenin
geçici dosyaya yazılıp yeni pencerede okunması ("Düzen kaydedildi ve yeniden yüklendi") gözle
kontrol edildi. **Panelleri fareyle sürükleyip bırakma gerçek pencerede denenmedi.**

## Yapılmayanlar

- Sayfa navigator'ının =/+ ağacı ve filtre (Faz 6 planı 6.1.5).
- Panellerin ayrı pencereye çıkarılması (yüzen pencere).
- Klemens, PLC ve kablo navigator'ları.
