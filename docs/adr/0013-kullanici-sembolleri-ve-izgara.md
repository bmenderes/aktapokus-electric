# ADR-0013: Kullanıcı sembolleri ve editör ızgarası

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16

## Bağlam

Proje sahibi üç şey istedi: hazır sembol kütüphanelerini (QElectroTech, SkyCAD, üretici DXF/DWG
kütüphaneleri) kullanabilmek, sembolleri kendi çizebileceği bir blok editörü ve çizim alanında ızgara.
Üçünün de ortak temeli, yerleşik parametrik sembollerden (ADR-0009) bağımsız, **veri olarak** tutulan
bir sembol tanımıdır. Bu ADR o temeli ve ızgarayı kapsar; blok editörünün ekranı ve içe aktarıcılar
bunun üzerine kurulacak.

## Kararlar

### Sembol tanımı

| Konu | Karar |
|---|---|
| Biçim | `SymbolDefinition`: ad, çizim öğeleri (çizgi, dikdörtgen, daire, yazı) ve bağlantı uçları. Ölçüler milimetre, başlangıç noktası ilk uç. |
| Uç | Konum ve telin çıktığı yön (dört ana yönden biri). Uç sırası function'ın bağlantı noktası sırasıdır. |
| Yer | Semboller projenin içinde saklanır (`symbols`); proje dosyası tek başına çizilebilir. Tek sembol `*.sembol.json` dosyası olarak da taşınır; biçim aynıdır. |
| Kaynak ve lisans | Her sembolde isteğe bağlı `source` ve `license` alanı vardır. İçe aktarılan semboller bunları doldurmak zorunda olacak (ADR-0001: üretici verisi dağıtılmaz). |
| Atama | Function bir sembole atıf yapabilir (`symbolId`). Atıf yoksa yerleşik parametrik sembol kullanılır; mevcut projeler değişmeden çizilir. |
| Uyum | Atanan sembolün uç sayısı function'ın bağlantı noktası sayısına eşit olmalıdır. Kullanılan bir sembolü uymayan bir sürümle değiştirmek reddedilir. |
| Doğrulama | Ad boş olamaz; en az bir uç; uçlar farklı noktada ve dört ana yönden birine bakar; koordinatlar sonlu; sıfır boyutlu öğe yok. |
| Sınırlar | Sembolün kutusu bütün öğeleri ve uçları kapsar; yazı genişliği yazı boyundan tahmin edilir. Seçim, tel yolu ve yerleşim bu kutuyu kullanır. |

### Command, patch ve dosya

- `PutSymbol` (ekle ya da aynı kimliklinin yerine koy) ve `AssignSymbol` (function'a sembol ata ya da kaldır).
  İkisi de yalnızca çizimi değiştirir, geri alınabilir.
- Patch: `assignSymbol` (function bağlantı noktasıyla, sembol adı ya da kimliğiyle).
- CLI: `ake symbol set <proje> <sembol.json>`.
- **Şema 4:** `symbols` ve function'a `symbolId`. İkisi de isteğe bağlı; şema 3 dosyası dönüştürülmeden yüklenir.
- Çizim öğesi türü JSON'da `type` alanıyla belirtilir (`line`, `rect`, `circle`, `text`).

### Otomatik yerleşimde değişen

Sayfada önceden yerleşmiş semboller varsa yeni semboller onların altından başlar. Önceden üst üste
biniyordu (bir patch ile eklenen cihaz var olan sigortanın üzerine yerleşti).

### Izgara

| Konu | Karar |
|---|---|
| Yer | Yalnızca editör ekranında, sayfanın çizim alanında. Çıktıya (PDF, SVG, PNG) girmez. |
| Adım | Sembollerin yapıştığı adımla aynı: 5 mm. Her ikinci nokta biraz daha koyu. |
| Yoğunluk | Noktalar ekranda 6 pikselden yakın düşerse adım ikiye katlanır; uzaklaştırınca alan griye dönmez. |

## Yapılmayanlar

- **Blok editörü ekranı.** Model, doğrulama, dosya biçimi ve çizim hazır; öğe ekleyip taşıyan,
  uç yerleştiren ekran yok. Şimdilik sembol JSON dosyası elle yazılarak tanımlanır.
- **İçe aktarıcılar:** QElectroTech `.elmt`, DXF blokları, SkyCAD. ROADMAP'teki "Sembol kütüphaneleri" bölümüne bakın.
- Yay (arc) ve çoklu çizgi (polyline) öğeleri. QElectroTech ve DXF sembollerinde sık geçer; içe
  aktarıcılarla birlikte eklenecek.
- Kütüphanedeki bir ürüne varsayılan sembol bağlamak (kütüphaneden eklenen cihazın sembolü kendiliğinden gelsin).

## Bilinen eksikler

- Sembol yazıları, şablonun yazı tipiyle çizilir; sembole özel yazı tipi yok.
- Yerleşim, kullanıcı sembolünün boyuna bakmadan satır aralığını sabit tutar; uzun bir sembol bir
  sonraki satıra taşabilir.
