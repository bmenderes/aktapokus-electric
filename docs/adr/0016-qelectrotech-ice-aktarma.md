# ADR-0016: QElectroTech eleman içe aktarıcısı

- **Durum:** Kabul edildi; gerçek koleksiyonla denendi (2026-11-16, aşağıda)
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0013 (sembol tanımı), Faz 4 ".elmt importer" maddesi

## Bağlam

Proje sahibi QElectroTech'in hazır sembol kütüphanesinin kullanılabilmesini istedi. İçe aktarıcı önce
biçimin bilinen yapısına göre ve elle yazılmış örnek elemanlarla (`examples/qelectrotech/`) yazıldı,
sonra gerçek koleksiyonla denenip düzeltildi (aşağıda).

## Kararlar

| Konu | Karar |
|---|---|
| Girdi | Tek `.elmt` dosyası ya da bir klasör (alt klasörler dahil). Klasördeki bozuk ya da lisanssız elemanlar raporlanır, diğerleri alınır. |
| Çıktı | Her eleman bir kullanıcı sembolü olur (ADR-0013); projeye tek transaction ile, kaynak `Import` olarak eklenir. |
| Ölçek | Varsayılan 0,5 mm/piksel: QElectroTech'in 10 piksellik ızgarası 5 mm olur, uçlar sayfa ızgarasına oturur. `--scale` ile değişir. |
| Başlangıç noktası | Elemanın sıcak noktası değil, **ilk ucu** (ADR-0013). |
| Uç yönü | `orientation` n/s/e/w → yukarı/aşağı/sağa/sola; telin uçtan çıktığı yön. |
| Çizim öğeleri | `line`, `rect`, `circle`, `text` doğrudan; `polygon` çizgilere (kapalıysa son kenar dahil); `ellipse` ve `arc` 16 parçalık tam tur oranında kısa çizgilere bölünür. Eşit eksenli tam elips daire olur. |
| Alınmayanlar | `dynamic_text` (cihaz etiketini uygulama kendisi yazar), `input` (eski biçim yazıları) ve tanınmayan öğeler. Çizgi stili, kalınlık, dolgu ve renk alınmaz. |
| Ad | `<names>` içinden Türkçe, yoksa İngilizce, yoksa ilk ad, yoksa dosya adı. |
| Kimlik | Dosyanın göreli yolundan türetilir. Aynı klasör yeniden alınınca semboller güncellenir, çoğalmaz. |
| Lisans | Sırayla: `--license`; koleksiyon kökündeki `ELEMENTS.LICENSE` (alınan yolun kendisinde ya da en çok dört üst klasörde; CC-BY 3.0 adlandırıyorsa); elemanın `<informations>` alanındaki lisans adı (büyük-küçük harf duyarlı: Almanca "mit" lisans sayılmaz). Hiçbiri yoksa eleman **alınmaz** (ADR-0001). Kaynak alanına dosya yolu ve bilgi metninin başı yazılır. |
| Yazı | Yazı boyu punto × ölçek × 0,55 (9 punto ≈ 2,5 mm). Çok satırlı yazı satırlara bölünür. |

### CLI

```text
ake symbol import <proje.json> <dosya.elmt | klasör> [--license <lisans>] [--scale <mm/piksel>]
```

## Gerçek koleksiyonla deneme (2026-11-16)

Proje sahibinin izniyle `qelectrotech/qelectrotech-elements` deposunun yalnızca `10_electric` klasörü
(7.190 eleman, 107 MB, 2026-11-07 tarihli sürüm) `Ornek/` altına indirildi; depoya girmez.

| Sonuç | Sayı | Açıklama |
|---|---|---|
| Alındı | 4.459 | 11 saniyede, tek komutla. |
| Ucu yok | 2.695 | Şema sembolü değil: WAGO ve diğer üreticilerin montaj görünüşleri (1.449 WAGO), montaj planı grafikleri, EN 60617 niteleyici işaretleri. Pano yerleşimi (Faz 8) için ayrıca değerlendirilebilir. |
| İki uç aynı noktada | 36 | Model buna izin vermez (ADR-0013); alınmadı. |

İlk denemede iki biçim farkı bulundu ve düzeltildi: lisans elemanlarda değil koleksiyon dosyasındaydı;
yazılar çok büyük ve çok satırlıydı. 16 sembollük bir katalog sayfası çizilip gözle denetlendi
(bobin, sigorta, devre kesici, GV, termik röle, motorlar, lamba, klemens, ayırıcı, kaçak akım, fren).

## Lisans (koleksiyonun `ELEMENTS.LICENSE` dosyasından)

- Elemanların şemalarda kullanımı, değiştirilmesi ve şemalara konması **koşulsuz serbesttir**; şemanın
  lisansı ne olursa olsun.
- Koleksiyonun tamamı ya da bir kısmı şema **dışında** yeniden dağıtılırsa **CC-BY 3.0** koşulları geçerlidir.
- Koleksiyon **makine öğrenmesi modeli eğitmek için örnek veri olarak kullanılamaz.** Bu, Faz 9'daki yapay
  zeka özelliği için bağlayıcıdır: içe aktarılan semboller modele eğitim ya da ince ayar verisi olarak
  verilmez. Lisans metni her sembolün `license` alanına bu kısıtla birlikte yazılır.
- Uygulama koleksiyonu dağıtmaz (ADR-0001); kullanıcı kendisi indirir ve içe aktarır.

## Açık konular

- **Bütün koleksiyonu tek projeye almak pratik değil:** 4.459 sembollük proje dosyası 60 MB oldu. Çözüldü:
  semboller artık paylaşılan kütüphanede tutulabiliyor ([ADR-0017](0017-ortak-sembol-kutuphanesi.md)).
  Şimdilik ihtiyaç duyulan klasör alınmalı (ör. `10_allpole/310_relays_contactors_contacts`). Kalıcı
  çözüm, sembolleri projeden ayrı, paylaşılan bir sembol kütüphanesinde tutmak ve projeye yalnızca
  kullanılanları kopyalamaktır.
- Bazı elemanlar kendi uç numaralarını yazı olarak taşır; uygulamanın yazdığı bağlantı noktası adıyla
  yan yana iki numara görünebilir.
- **Ürüne bağlama yok.** İçe aktarılan sembol bir function'a elle ya da patch ile atanır; kütüphane
  ürününe varsayılan sembol bağlanmaz.
- Yay ve elips parçalara bölündüğü için blok editöründe tek öğe olarak değil, çok sayıda çizgi olarak
  düzenlenir.
