# Faz 6 — Etkileşimli editör: kapsam ve plan

Durum: **onaylandı** (2026-11-16)

## Neden yeniden ele alıyoruz

Proje sahibinin değerlendirmesi: arayüz profesyonel görünmüyor, alışılmış E-CAD araçlarını
andırmıyor. Bugünkü pencerenin (ADR-0010, ADR-0014) somut sorunları:

| Sorun | Bugün | Hedef |
|---|---|---|
| Yazı tipi | Arayüzde serifli Lora; masaüstü mühendislik yazılımlarında alışılmış değil | Gömülü, sade, sıkı bir sans yazı tipi (ör. Inter, OFL); 9–10 pt yoğunluk |
| Komutlar | Yazılı büyük düğmeler, az komut | İkonlu, gruplanmış komut şeridi (sekmeler: Başlangıç, Ekle, Bağlantı, Sayfa, Görünüm, Araçlar) ya da ikonlu menü + araç çubuğu |
| Paneller | Sabit; sekmeleri düz yazı | Başlık çubuklu, taşınabilir, kapatılıp açılabilen, yeri hatırlanan paneller (Dock.Avalonia, MIT) |
| Sayfa gezgini | Düz liste | =/+ yapısına göre ağaç (Fonksiyon > Yer > Sayfa), ikonlar, filtre |
| Çizim alanı | Tek sayfa | Sekmeli: birden çok sayfa açık; sayfa sekmesinde numara + başlık |
| Durum çubuğu | Tek satır metin | Bölmeli: imleç X/Y (mm), ızgara/yakalama açık-kapalı, seçili nesne, ERC özeti |
| Renk ve yoğunluk | Geniş boşluklar, gri düz zemin | Sıkı satır aralığı, ince ayraçlar, seçimde vurgu rengi; açık tema |

## Kapsam (iş kalemleri; her biri ayrı commit + test + gerekirse ADR)

### 6.1 Görünüm ve kabuk
1. Arayüz yazı tipi, renk ve yoğunluk teması (tek kaynak: `Themes/` sözlüğü).
2. Komut şeridi ya da ikonlu menü + araç çubuğu; komutlar tek bir komut kaydından (ad, ikon, kısayol, etkinlik koşulu) beslenir.
3. Dock altyapısı: navigator'lar, mesajlar ve özellikler taşınabilir panel; düzen kullanıcı profiline kaydedilir; "Varsayılan düzene dön".
4. Sekmeli sayfa düzenleyici (birden çok sayfa).
5. Sayfa navigator'ı ağaç görünümü (=/+ → sayfa) ve filtre. **Yapıldı** (fonksiyon yolu > yer yolu > sayfa; süzgeç sayfa adı ve grup adında, Türkçe harflere duyarsız).
6. Bölmeli durum çubuğu (imleç mm, ızgara, yakalama, seçim, ERC).

### 6.2 Düzenleme
7. Kütüphaneden ya da Cihazlar navigator'ındaki yerleştirilmemiş function'dan sayfaya **sürükle-bırak** → `AddDevice` / `AddPlacement`.
8. **Tel çekme**: uçtan uca tıklama, uca yakalama, önizleme → `Connect`. **Köşe noktası** yapıldı (ADR-0029): çekerken boş yere tıklamak köşe noktası verir; sonradan sağ tık > Köşe noktaları ver / Otomatik yönlendir.
9. Silme (yerleşim ya da cihaz), çoklu seçim (dikdörtgen), kopyala/yapıştır (yeni tag atanır).
10. Kaydedilmemiş değişiklikle kapatırken uyarı; Yeni proje, Farklı kaydet. **Yapıldı** (yeni proje uygulamayla gelen A3 şablonla tek sayfa açılır; çıkış, yeni proje ve aç kaydetmeyi sorar).
11. Özellik penceresi sekmeli: tag, açıklama, ürün, yapı düğümü, bağlantı noktası adları (yeni command'lar). **Yapıldı** (Genel / Ürün / Noktalar; `SetDeviceDetails`, `SetFunctionDescription`, `RenameConnectionPoint`; Tamam tek geri alma adımı).
12. Sağ tık menüleri; çapraz referansa tıklayınca hedef sayfaya atlama. **Yapıldı** (ayna tablosu, uç notu ve kontağın altındaki bobin yeri `LinkItem` olur: editörde çift tıkla gidilir, PDF'te de bağlantıdır; sağ tık menüsü sembole, grafiğe, tele ya da boş alana göre komutları listeler).

### 6.3 Navigator'lar ve araçlar
13. Klemens, PLC ve kablo navigator'ları. **Yapıldı**: alt panelde Mesajlar, Potansiyeller, Klemensler, PLC, Kablolar sekmeleri (solda yalnızca Sayfalar, Cihazlar, Semboller). Satıra çift tıklayınca çizildiği yere gidilir. Klemens şeridi düzenleyicisi kaldı.
14. Kısayol profili (JSON, yaygın E-CAD alışkanlığına uygun varsayılan set). **Yapıldı**: Araçlar > Kısayollar penceresi, `kisayollar.json` (yalnızca değişenler), çakışma denetimi; biçim ve varsayılan set `docs/kisayollar.md`.
15. Performans: 300 sayfalık sentetik projede açılış ve sayfa geçişi ölçümü. **Yapıldı** (`tools/Ake.Bench`, sonuçlar ve bulunan darboğazlar `docs/performans.md`).

## Etkilenen modüller

- `Ake.Editor` (büyük kısmı), `tools/Ake.EditorSnapshot` (yeni panellerin görüntüsü).
- `Ake.Core`: yeni command'lar (açıklama, yapı düğümü, bağlantı noktası adı, yerleşim silme). Core bağımlılıksız kalır.
- `Ake.Render`: seçim/önizleme çizimi için küçük eklemeler; çıktı değişmez.
- Yeni NuGet: Dock.Avalonia (MIT), ikon seti (Fluent UI System Icons, MIT) — lisanslar eklenmeden önce yeniden kontrol edilir.

## Doğrulama yöntemi

Masaüstünde fare/klavye kullanılmaz. Her görsel adım başsız görüntü aracıyla (`tools/Ake.EditorSnapshot`)
PNG olarak alınır ve proje sahibine gönderilir; davranış `EditorSession` testleriyle doğrulanır.
Faz sonunda referans makine editörde baştan çizilir (DoD).

## Verilen kararlar (2026-11-16)

1. Komutlar **sekmeli komut şeridinde** (ribbon): Başlangıç, Ekle, Bağlantı, Sayfa, Görünüm, Araçlar.
2. Arayüz yazı tipi **Inter** (gömülü, OFL). Lora arayüzden kalkar (ADR-0010'daki yazı tipi kararının yerine yeni ADR).
3. Çizim alanının varsayılan zemini **beyaz kâğıt**, çevresi gri; Görünüm'den koyu zemine geçilebilir.

## Önerilen sıra

6.1 (1 → 6) önce: görünüm sorunu en çok onu rahatsız ediyor ve sonraki işler bu kabuğun üstüne
oturuyor. Her adımın sonunda ekran görüntüsü gönderilir; beğenilmeyen yön erkenden düzeltilir.

## Yaygın E-CAD özellikleri: kapsam listesi (2026-11-16)

Proje sahibi standart özellikleri tek tek saymıyor, eksiksiz olmasını bekliyor. Aşağıdaki liste,
alışılmış E-CAD iş akışının gerektirdiği özelliklerin hangi fazda karşılanacağını gösterir; yeni bir
iş başlarken buradan bakılır.

| Özellik | Durum / faz |
|---|---|
| Sayfa türleri: çok hatlı şema, tek hatlı, kapak, içindekiler, grafik raporlar | İçindekiler var (PDF); diğer sayfa türleri 6.3 |
| **Grafik rapor sayfaları** (klemens şeması, kablo şeması, BOM, PLC kartı genel görünüşü) projeye sayfa olarak üretilir | Raporlar tablo olarak var (Faz 3); sayfa olarak 6.3 |
| Makrolar: pencere/sayfa makrosu, değer kümeleri, makro kütüphanesi | 6.2 sonrası (kopyala/yapıştır üzerine) |
| Kesinti noktası (sayfalar arası potansiyel oku), bağlantı tanım noktası, köprü (jumper), T-bağlantı yönü | Çapraz referans notu var; ayrı semboller 6.2 |
| Kara kutu (black box), cihaz bağlantı noktası, PLC kutusu | PLC kartı çerçevesi var; düzenleme 6.2 |
| Klemens şeridi düzenleyici (sıra, köprü, üst/alt, sonlandırıcı) ve klemens navigator'ı | 6.3 |
| Kablo tanımı, damar atama, kablo navigator'ı | Model var; editör 6.3 |
| Parça (ürün) seçimi ve cihaza atama, parça yönetimi | Kütüphane var (Faz 4); seçim penceresi 6.2 |
| Numaralandırma: cihaz, tel, klemens; sayfa bazlı şema | Var (Faz 2, ADR-0021); editör komutları var |
| Proje kontrolü (ERC) ve mesaj yönetimi | Var; kural ayarları 6.3 |
| Revizyon yönetimi (değişiklik işaretleri, revizyon tablosu) | Audit log var; görünüm Faz 6 sonrası |
| Yapı tanımlayıcıları (=, +) yönetimi, sayfa ağacı | Model var; ağaç 6.1.5 |
| Pano yerleşimi (2D/3D), delik planı | Faz 8 |
| PLC dışa/içe aktarma | Faz 7 |
| Çeviri (çok dilli metinler) | Faz 6 sonrası |
| Yapay zeka asistanı | Faz 9 |
| Simülasyon | Faz 10 (aday) |

## Blok editörü şeridi (2026-11-18)

Blok editörü artık ana pencereyle aynı komut şeridini kullanır: `RibbonView` bir `RibbonDefinition` (komutlar, sekmeler, hızlı erişim,
Dosya menüsü) alır; ana pencere `EditorCommands.Main`, blok editörü `SymbolEditorCommands.Ribbon` verir. Blok editörü şeridi tek sekmedir
(Çizim): Araç (Seç, Çizgi, Dikdörtgen, Daire, Yazı, Uç; seçili olan vurgulu), Düzen (Geri al, Yinele, Sil), Seçim (Seçimi bırak, Ucu çevir,
İlk uç yap), Yeni ucun yönü (dört yön). Projeye kaydet / Dosyaya kaydet / Kapat Dosya menüsünde; Projeye kaydet, Geri al ve Yinele hızlı
erişim çubuğunda. Tek harfli araç kısayolları (S, L, D, C, T, U), Ctrl+Z/Y/S ve Delete (ve Backspace) değişmedi; blok editörünün
kısayolları sabittir (kısayol profili ana pencere içindir). Ad, yazı aracının metni ve function'a atama şeride sığmayan girdiler
olarak altta kalır.
