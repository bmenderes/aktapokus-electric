# ADR-0023: Komut şeridi ve editör teması

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0010'daki yazı tipi kararının (Lora) ve ADR-0014'teki menü + yazılı araç çubuğu düzeninin yerini alır. Plan: [docs/faz-6-editor.md](../faz-6-editor.md).

## Bağlam

Proje sahibi editörün profesyonel görünmediğini ve alışılmış E-CAD araçlarını andırmadığını söyledi.
Seçenekler sorulunca **sekmeli komut şeridi**, **Inter** yazı tipi ve **beyaz kâğıt** zemini seçti.

## Kararlar

| Konu | Karar |
|---|---|
| Komut kaydı | `EditorCommands`: her komutun kimliği, Türkçe adı, ikonu, kısayolu ve açık/kapalı olup olmadığı tek yerde. Şerit, Dosya menüsü, hızlı erişim ve kısayollar buradan kurulur; pencere yalnızca eylemleri ve etkinlik koşullarını bağlar. Arayüzsüz test edilir. |
| Şerit | Solda Dosya düğmesi (Aç, Kaydet, Çıkış), sekmeler **Başlangıç, Ekle, Bağlantı, Sayfa, Görünüm, Araçlar**, sağda hızlı erişim (Kaydet, Geri al, Yinele). Gruplar adlı ve ayraçlı; büyük düğmede ikon üstte, küçükler üçerli sütunda. Yapılamayan komut soluk görünür, açık/kapalı komut (ızgara, tag şeması) basılı görünür. Kendi kontrolümüz (`RibbonView`); Avalonia için olgun ve ücretsiz bir şerit kütüphanesi yok. |
| Kısayollar | Komutların tanımlı tuşları (Ctrl+O/S/Z/Y/E, R, Enter, Esc, Home, PageUp/PageDown). Yapılamayan komutun tuşu yutulmaz; metin kutusuna yazarken devreye girmez. |
| İkonlar | Fluent UI System Icons (`FluentIcons.Avalonia`, MIT). |
| Yazı tipi | **Inter** (`Avalonia.Fonts.Inter`, paket MIT, yazı tipi OFL), 12 px; Lora ve gömülü dosyası kaldırıldı. Çizimdeki yazılar yine şablonun yazı tipini kullanır. |
| Tema | Fluent, açık, sıkı yoğunluk. Renkler `App.axaml`'da tek yerde: pencere #F3F4F6, panel beyaz, başlık #E9ECF0, çizgi #D5D9DF, vurgu #1F6FD8. |
| Paneller | Başlık çubuklu çerçeveler (Navigator, Mesajlar); çizim alanının üstünde sayfa sekmesi ("Sayfa 2  Motor 1"). Kâğıt beyaz, çevresi açık gri. |
| Durum çubuğu | Bölmeli: bildirim ve proje özeti, imleç X/Y (mm), ızgara açık/kapalı, ERC özeti. |
| Yeni komut | Bağlantı > Tel numaraları (potansiyel başına; tek adımda geri alınır). |
| Çalışma alanı | Proje sahibi "kullanım alanı düşük" dedi. Pencere **tam ekran** açılır; Mesajlar paneli 140 px; sayfa kenar payı 8 px. Görünüm > Paneller: Navigator ve Mesajlar aç/kapa (ayraçla verilen boyut hatırlanır), **Şeridi daralt** (Ctrl+F1; daraltılmışken bir sekmeye tıklamak açar), **Tam çizim alanı** (F11: ikisini kapatır, şeridi daraltır; yeniden basınca önceki hâl). |

## Doğrulama

Başsız görüntü aracıyla referans makine açıldı: şerit, ikonlar, soluk komutlar, paneller ve durum
çubuğu gözle kontrol edildi. Komut kaydının tutarlılığı ve tel numaralama testlerle doğrulandı.
Şerit düğmelerine fareyle tıklama ve sekme değiştirme gerçek pencerede denenmedi.

## Yapılmayanlar (Faz 6 planında sırada)

- Taşınabilir paneller (Dock), sekmeli çoklu sayfa, sayfa ağacı (=/+), filtre.
- Blok editörü ve özellik penceresi henüz eski yazılı düğmelerle; şerit görünümüne geçirilecek.
- Koyu zemin seçeneği (Görünüm sekmesine eklenecek).
- Kısayol profilinin dosyadan değiştirilmesi.
