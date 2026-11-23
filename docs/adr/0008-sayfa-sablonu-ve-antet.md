# ADR-0008: Sayfa şablonu, antet ve çıktı (Faz 5, birinci kısım)

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15
- **Not:** Proje sahibinin isteğiyle plan onayı beklenmeden yazıldı. Faz 5'in tamamı değil, antet ve
  sayfa çıktısı kısmıdır; proje sahibi bunu "mutlak" gereksinim olarak belirtti.

## Bağlam

Yaygın E-CAD çıktılarında her sayfa aynı çerçeveyi taşır: üstte 0–9 kolon bandı, yanlarda A–F
satır bandı, altta firma logosu ve proje bilgileriyle antet. Kullanıcı kendi antetini
tanımlayabilmelidir; sabit, koda gömülü bir antet yeterli değildir.

## Kararlar

### Model

| Konu | Karar |
|---|---|
| Şablon veridir | `SheetTemplate` kağıt boyutunu, kenar boşluğunu, kolon/satır sayısını, bant genişliğini, antet yüksekliğini, yazı tipini ve antet hücrelerini tutar. Kod değil, düzenlenebilir veridir. |
| Projenin içinde | Şablonlar proje dosyasında saklanır; proje tek başına çıktı verebilir. Aynı biçim ayrı dosya olarak da okunur (`*.sablon.json`), böylece şablon projeler arasında taşınır. |
| Varsayılan | Sayfanın kendi şablonu (`templateId`) yoksa projenin **ilk** şablonu kullanılır. Hiç şablon yoksa çıktı alınmaz (`render.sablon_yok`); gizli bir yerleşik şablon yoktur. |
| Antet hücresi | Antetin sol üst köşesine göre konum ve ölçü (mm), isteğe bağlı küçük etiket, içerik, yazı boyutu, hizalama, kenarlık ve görsel. Hücreler serbest yerleşir; ızgara zorunluluğu yoktur. |
| Yer tutucular | İçerikte `{project.name}`, `{project.<alan>}`, `{page.number}`, `{page.title}`, `{page.count}`, `{page.next}`, `{page.function}`, `{page.location}`, `{today}`. Tanımsız proje alanı boş çıkar; **bilinmeyen** yer tutucu olduğu gibi kalır ki yazım hatası fark edilsin. |
| Proje alanları | `Project.Properties`: serbest anahtar/değer (çizim no, revizyon, çizen, tarih…). Anahtara göre sıralı saklanır. |
| Logo | Hücre bir görsel dosyasına atıf yapar (`imageRef`), yol proje dosyasının klasörüne göredir. Görsel proje dosyasına gömülmez. Dosya yoksa hücre boş çizilir, çıktı engellenmez. |
| Yazı tipi | Şablonda açıkça yazılır; sistem varsayılanına bırakılmaz. Makinede yoksa SkiaSharp yakın bir yazı tipine düşer. |

### Command ve dosya

| Konu | Karar |
|---|---|
| Command'lar | `PutSheetTemplate` (ekle ya da aynı kimliklinin yerine koy) ve `SetProjectProperty` (ayarla ya da sil). İkisi de geri alınabilir. |
| Patch | `setProperty` komutu eklendi. Şablon patch ile değil, `ake template set` ile konur. |
| Şema 3 | Proje dosyasına `properties`, `sheetTemplates` ve sayfaya `templateId` eklendi. Hepsi isteğe bağlı; şema 2 dosyası veri dönüştürülmeden yüklenir. |

### Çizim

| Konu | Karar |
|---|---|
| Sahne | Çizim önce biçimden bağımsız bir sahneye (çizgi, dikdörtgen, metin, görsel; milimetre) çevrilir. SVG, PDF ve PNG aynı sahneyi yazar. Şema içeriği eklendiğinde aynı sahneye girecek. |
| Geometri | `SheetGeometry` çerçeveyi, bantları, çizim alanını ve anteti şablondan hesaplar. Kolon genişliği ve satır yüksekliği çizim alanına bölünerek bulunur; sembol yerleşimi ve `sayfa.kolon` çapraz referansı bunu kullanacak. |
| PDF | SkiaSharp `SKDocument`, sayfa başına bir sahne (ADR-0001). |
| SVG | Metin tabanlı ve deterministik; snapshot testi SVG üzerinden yapılır. |
| PNG | Önizleme ve gözle doğrulama için. Yol haritasında yoktu; çıktıyı görmeden doğrulamak mümkün olmadığı için eklendi. |

### CLI

```text
ake template set <proje.json> <sablon.json>
ake render <proje.json> [--pdf <dosya>] [--svg <klasör>] [--png <klasör>]
```

## Yapılmayanlar (Faz 5'in devamı)

- **Şema içeriği:** semboller, teller, tel numaraları, çapraz referanslar. Çizim alanı şu an boştur.
- **Auto-layout** ve devre tipi şablonları.
- **Sayfa bazlı tag şeması** (`3F3`) ve `sayfa.kolon` çapraz referansları.
- **DXF çıktısı**, PDF içindekiler ve yer imleri.
- **DWG** (karar: denenecek; bkz. ROADMAP).
- **Antet editörü:** şablon şimdilik JSON dosyası elle düzenlenerek yapılır. Görsel editör Faz 6'dadır.

## Bilinen eksikler

- **Metin taşması denetlenmez.** Hücreye sığmayan metin kesilmez ve küçültülmez; komşu hücrenin
  üzerine yazar.
- **Sayfa açıklaması tek alandır** (`{page.title}`). Örnek projede antet iki satır açıklama taşır;
  şimdilik başlığa satır sonu konarak yazılır.
- **PDF boyutu:** üç sayfalık çıktı yaklaşık 570 KB, çünkü yazı tipi gömülüyor. Yüzlerce sayfalık bir
  projede boyutun nasıl büyüdüğü ölçülmedi.
- **Yazı tipi yedeği sessizdir:** şablondaki yazı tipi makinede yoksa uyarı verilmez.
- **PDF yalnızca sayfa sayısı ve başlık imzasıyla test edildi**; içeriğin görsel doğruluğu PNG
  çıktısına bakılarak elle kontrol edildi.
