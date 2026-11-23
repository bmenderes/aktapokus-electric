# ADR-0052: Pnömatik devre şeması

Durum: kabul edildi (2026-11-19, proje sahibi: "pnömatik devre şeması"; Faz 6c madde 6)

## Bağlam
Makine projelerinde elektrik şemasının yanında pnömatik devre de çizilir: kompresör, hazırlama grubu, valfler, silindirler, hava hatları. Yeni bir çizim motoru gerekmez; mevcut model (cihaz, function, uç, bağlantı türü, sembol) hava hattını da taşır. Eksik olan pnömatik semboller, hat türleri ve uç öznitelikleridir.

## Karar
- **Uç türü:** `PinKind.Pneumatic` (hava ucu) ve `ConnectorType.Thread` (dişli bağlantı; ölçü `ConnectorCode`'da: `G1/4`, `M5`…). Push-in fitting için mevcut `PushIn` + `Ø6 mm` kullanılır. Arayüz etiketleri: "Pnömatik (hava)", "Dişli (G, M, NPT)". Dosya biçimi değişmez (enum değerleri metin olarak yazılır).
- **Hat türleri** (hazır bağlantı türleri, ISO 1219-1 çizgi biçimleriyle): **Hava hattı** (düz, mavi), **Pilot hattı** (kesik, mavi), **Egzoz hattı** (noktalı, gri-mavi); kategori Pnömatik. Şemada otomatik gösterge olarak listelenir (ADR-0036).
- **Standart semboller** (palet kategorisi "Pnömatik", 14 sembol, ISO 1219-1'in sadeleştirilmiş, bu projeye özgü elle çizilmiş biçimi; Apache-2.0): Kompresör, Hava deposu, Hava hazırlama ünitesi (FRL), Filtre, Basınç regülatörü, Manometre, Susturucu (egzoz), Çek valf, Tek yönlü hız kontrol valfi, 3/2 valf (selenoid/yay, NC), 5/2 valf (selenoid/yay), 5/2 valf (çift selenoid, hafızalı), Silindir (çift etkili), Silindir (tek etkili, yaylı).
- **Uç adları ISO 5599/1219'a uyar:** 1 basınç, 2 ve 4 çalışma, 3 ve 5 egzoz, silindirde A (kapak) ve B (mil tarafı). Valf sembollerinde uç sırası 4-2-5-1-3 (5/2) ve 2-1-3 (3/2); function'ın bağlantı noktaları aynı sırayla adlandırılır.
- **Elektrik ve pnömatik tek cihazda:** bir valf cihazı iki function taşır: pnömatik gövde (hava uçları) ve selenoid bobini (mevcut "Selenoid valf bobini" sembolü, uçlar A1/A2). Bobin elektrik şemasında, gövde pnömatik şemada çizilir; çapraz referans (ADR-0019) ikisini bağlar.
- **Örnek proje:** `examples/pnomatik/pnomatik.json` — kompresör → FRL → 5/2 selenoid valf → hız kontrol valfleri → çift etkili silindir, egzozlar susturuculu; ERC temiz, otomatik yerleşimle çizilir. Test (`PneumaticExampleTests`) projeyi sıfırdan kurar; `AKE_WRITE_EXAMPLES=1` dosyayı yeniden yazar.

## Bu dilimde olmayanlar
Pnömatik ERC kuralları (basınç/egzoz uyumu, tek uca iki besleme), devre tipini tanıyan pnömatik otomatik yerleşim (şu an genel yerleşim tek satırdır), boru ölçüsü ve uzunluğu için ayrı alanlar, valf/silindir ürün kütüphanesi (üretici ürünleri), pilot (hava ile tahrikli) valf sembolleri, pnömatik adım diyagramı (zaman diyagramı).
