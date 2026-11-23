# ADR-0020: PDF içindekiler ve yer imleri

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0001 (SkiaSharp ile PDF), ADR-0008 (sayfa şablonu ve antet)

## Bağlam

Teslim edilen şema PDF'inde okuyucu sayfalar arasında hızlı gezinebilmeli: görüntüleyicinin kenar
çubuğunda yer imleri ve başta tıklanabilir bir içindekiler sayfası. SkiaSharp PDF'e bağlantı ve isimli
hedef yazabiliyor, ama yer imi (outline) yazamıyor. Yeni bir PDF kütüphanesi eklemek istemiyoruz
(QuestPDF yasak, çoğu alternatif GPL/AGPL ya da ticari).

## Kararlar

| Konu | Karar |
|---|---|
| Yer imleri | Her sayfanın sahnesi bir `Bookmark` taşır (`numara  başlık`). PDF SkiaSharp ile yazıldıktan sonra dosyanın sonuna **artımlı güncelleme** eklenir: `/Outlines` nesneleri, `/Outlines` ve `/PageMode /UseOutlines` eklenmiş katalog, `/Prev` ile öncekine bağlı yeni xref tablosu (`PdfOutline`). Önceki baytlar değişmez; standart PDF mekanizmasıdır. Başlıklar UTF-16BE yazılır (Türkçe harfler). |
| İçindekiler | `SheetRenderer.Contents`: projenin ilk şablonunun çerçevesi ve antetiyle, Sayfa / Başlık / Fonksiyon / Yer sütunlu liste. Sığmazsa sonraki sayfalara sürer; sayfa numaraları `İ1`, `İ2`. Modelde sayfa değildir, çıktı anında üretilir. |
| Bağlantılar | Her içindekiler satırı görünmez bir `LinkItem`'dır; PDF'te sayfanın isimli hedefine (`Scene.Anchor`, `sayfa-<id>`) gider. SVG, PNG ve editör bunu yok sayar. |
| Komut | `ake render ... --icindekiler`: içindekiler PDF'in başına eklenir; `--png`/`--svg` ile `icindekiler-N` dosyaları da yazılır. Seçenek verilmezse çıktı eskisi gibidir (yer imleri her zaman eklenir). |
| Biçim varsayımı | `PdfOutline` SkiaSharp'ın klasik xref tablosunu bekler; beklenmedik biçimde programcı hatası olarak exception fırlatır (ADR-0002). SkiaSharp güncellemesinde testler bunu yakalar. |

## Doğrulama

Referans makine `--pdf --icindekiler` ile yazıldı; yeni xref tablosundaki her konumun ilgili nesnenin
başını gösterdiği bayt düzeyinde denetlendi, içindekiler sayfası PNG olarak gözle kontrol edildi.
Testler: `tests/Ake.Render.Tests/PdfContentsTests.cs`, CLI testi. Gerçek bir PDF görüntüleyicide
yer imi paneli bu ortamda açılıp görülemedi; kullanıcı denetimi bekliyor.

## Yapılmayanlar

- İç içe yer imleri (fonksiyona `=` ya da yere `+` göre gruplama).
- İçindekiler antetinde "sonraki sayfa" boş kalır.
- Çapraz referans yazılarının (`-K1:14 / 2.1`) PDF'te tıklanabilir olması.
