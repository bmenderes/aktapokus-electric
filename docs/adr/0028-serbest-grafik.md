# ADR-0028: Serbest grafik ve PDF'ten içe aktarma

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-17
- **İlişki:** ADR-0004 (command), ADR-0005 (proje dosyası), ADR-0022 (DXF/DWG), ADR-0027 (DXF/DWG'den sembol);
  plan: `docs/pdf-ice-aktarma-plani.md`

## Bağlam

Başka bir E-CAD aracında çizilmiş eski projeler (elde yalnızca PDF olsa bile) bu uygulamada düzenlenebilir
açılmalı; böylece projelerdeki birikim kaybolmaz. İncelenen gerçek PDF vektör, metni gerçek ve yer imli
(`docs/pdf-ice-aktarma-plani.md`). Çizim bu araçtaki elektriksel modele doğrudan çevrilemez; önce olduğu gibi
alınabilmesi gerekir. Aynı nesne, DXF/DWG çiziminin sayfaya alınması için de bekleniyordu (ROADMAP).

## Karar

| Konu | Karar |
|---|---|
| Model | `PageGraphic`: sayfaya bağlı **çoklu çizgi, daire, yazı**. Elektriksel anlamı yoktur: cihaz, bağlantı, potansiyel ve ERC kapsamı dışındadır. |
| Şekil | Değişmez kayıtlar (`GraphicPolyline`, `GraphicCircle`, `GraphicText`). Koordinat sayfa koordinatıdır (mm, y aşağı). Renk `#rrggbb` ya da boş (varsayılan siyah). Çizgi kalınlığı, dolgu ve (yazıda) hizalama şekilde durur. |
| Command | `AddGraphics` (toplu; geçersiz biri varsa hiçbiri eklenmez, tek geri alma adımı), `RemoveGraphics`, `ReplaceGraphicShapes` (taşıma, yazı düzenleme, renk). Model yalnızca bunlarla değişir. |
| Çoklu çizgi | Bir PDF sayfası binlerce çizgi parçası içerir; bağlı parçalar çoklu çizgi olarak tutulur. Noktalar dosyada tek metin: `"x,y x,y…"` (üç ondalık, değişmez kültür); böylece dosya küçük kalır ve satır başına bir sayı yazılmaz. |
| Dosya biçimi | Şema 5 → **6**: `graphics` listesi (isteğe bağlı; yoksa grafik yok). Eski dosyalar göçsüz açılır. Grafiği olmayan proje eskisiyle aynı metni üretir (liste yazılmaz). |
| Doğrulama | Kimlikler tekil; sayfa var; şekil geçerli (en az iki nokta, sonlu sayı, pozitif yarıçap ve boy, tek satır yazı, geçerli renk). Dosya yüklenirken de aynı denetim çalışır. |
| Yazı tek satır | Çok satırlı kaynak yazı satırlara bölünür; her satır ayrı yazıdır. Böylece düzenleme basit kalır. |

### PDF okuyucu (uygulandı)

| Konu | Karar |
|---|---|
| Kütüphane | **PdfPig 0.1.16** (Apache-2.0, saf C#, net9.0 için bağımlılığı yok). `Ake.Library`: `PdfImport.Read` ve `PdfImport.Plan`. Python ya da PyMuPDF (AGPL) uygulamaya girmez. |
| Çizgi ve eğri | Her yol bir çoklu çizgi olur; uç uca gelen ve aynı renk/kalınlıktaki ardışık çizgiler tek çoklu çizgiye birleşir. Bezier eğrileri 8 parçaya bölünür. Beyaz (kâğıdı maskeleyen) çizimler alınmaz. |
| Kalınlık | PdfPig, çizim matrisinin ölçeğini kalınlığa uygulamaz. Belgedeki **ortanca kalınlık 0,25 mm** olur, diğerleri oranını korur (0,1–1,5 mm). |
| Yazı | Aynı satırdaki, aynı boydaki, aynı yöndeki bitişik harfler bir yazı olur; bitişik değilse ayrı yazıdır ("U V W"). **Dönüş açısı** korunur (`GraphicText.RotationDeg`; kaynakta dik yazılar çok). |
| Çerçeve | Sayfa boyunun %70'inden uzun yatay ve dikey çizgilerin ayrık koordinatlarından, dıştan ikincisi iç çerçeve (çizim alanı) olur; sayfaların en az üçte birinde uyuşuyorsa belge çerçevesi sayılır. Çerçeveye değmeyen her şey kalır; çerçeve, kolon/satır harfleri ve antet atılır. İçerik, projenin şablonunun çizim alanına kaydırılır. `--ramkayi-koru` hepsini alır. |
| Sayfa başlığı | PDF yer imlerinden (kategorili ağaçta kökün çocukları, düz listede kökün kendisi); yoksa "Sayfa n". Cihaz listesi düğümleri alınmaz (Aşama 3 için ipucudur). |
| Numara | Projenin son sayfasından devam eder. |
| Komutlar | `ake import pdf <proje> <pdf> [--ramkayi-koru] [--dry-run]`; editörde Dosya > "PDF'ten al" (arka planda okur; proje açık değilse boş proje açar). Tek geri alma adımı. |

## Sonuçlar

- Onlarca sayfalık gerçek bir E-CAD PDF'i **birkaç saniyede** okundu: on binlerce grafik (çoğu çoklu çizgi, bir kısmı yazı),
  çerçeve öğeleri atıldı, görseller alınmadı. Proje dosyası onlarca MB'ı geçmedi; açılışı yaklaşık 0,3 saniye uzadı.
- Aktarılan sayfa (sürücü, motor, renkli kablolar, dik yazılar) PNG'ye çizilip kaynak sayfayla karşılaştırıldı:
  konum, ölçü, renk ve yazılar aynı. Sayfa başlığı yer iminden geldi.

- Serbest grafik **yer imi, çapraz referans ya da rapor** üretmez; yalnızca çizilir, seçilir, taşınır ve çıktıya
  (PDF, SVG, DXF, DWG) girer.
- İçe aktarılan çizim ileride modele tanınarak çevrilebilir (plan, Aşama 3); o zaman ilgili grafikler modelle
  değiştirilir.
- Bellek: onlarca sayfalık bir proje yüz binlerce parçadır; çoklu çizgi birleştirmesi bu sayıyı birkaç on bine indirir.
  Her komut `IdSet` hesapladığı için çok büyük projelerde ölçülecek (Faz 6.3 performans maddesi).

## Yapılmayanlar

- Yay ve eğri (şimdilik çizgi parçası ve daire), **görsel** (PDF'teki logo ve resimler alınmaz), tarama.
- **Dolgu** yalnızca dairede çizilir; dolgulu çokgen (ok başı) çerçevesiyle görünür.
- Taranmış (görüntü) PDF'ten çizim çıkmaz; OCR yok.
- Okuyucu çerçeveyi yalnızca uzun çizgilerden tanır; çerçevesi dikdörtgen olarak çizilmiş PDF'te çerçeve tespit edilmez
  (içerik çerçevesiyle gelir, `--ramkayi-koru` ile aynı).
- Dönüşü 90'ın katı olmayan yazının seçim kutusu yaklaşıktır.
- Gruplama ve katman (içe aktarmada kaynaktaki renk yeterli).
- Grafiğin elektriksel modele tanınarak çevrilmesi: Aşama 3.
