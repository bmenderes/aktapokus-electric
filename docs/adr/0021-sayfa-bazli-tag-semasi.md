# ADR-0021: Sayfa bazlı tag şeması

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0005 (numaralandırma), ADR-0006 (raporlarda tag yazımı)

## Bağlam

Sayfa bazlı tag şemasında tag'ler sayfa numarasıyla başlar: `3F3` = 3. sayfa, F, o sayfadaki 3. F.
Numaralandırma servisi yalnızca "harf + sıra" (`K1`) biçimini biliyordu. ROADMAP, şemanın proje
ayarı olmasını istiyor.

## Kararlar

| Konu | Karar |
|---|---|
| Ayar | `Project.TagScheme`: `LetterNumber` (varsayılan, `K1`) ya da `PageLetterNumber` (`3K1`). Proje dosyasında `tagScheme`; varsayılanda yazılmaz. Şema 5; eski dosyalar için dönüştürülecek veri yok. |
| Tag'in kendisi | Sayfa numarası **tag metninin parçasıdır** (`ProductTag = "3K1"`); ayrı bir gösterim katmanı yok. Böylece çizim, raporlar, ERC ve dışa aktarımlar değişmeden yeni tag'i kullanır; tam gösterim `=M1+P1-3K1` olur. |
| Cihazın sayfası | Function'larının ana görünümleri arasında sayfa sırası en küçük olanın sayfası. Hiçbir sayfada olmayan cihaza dokunulmaz. |
| Sıra | Aynı sayfa ve aynı harf içinde sembolün yerine göre: önce soldan sağa (X), sonra yukarıdan aşağı (Y), eşitse model sırası. Sayfa bazlı şemada yapı düğümleri sırayı ayırmaz; sayfa numarası tag'i zaten tekil yapar. |
| Harfin okunması | Önce projedeki sayfa numaralarından en uzun eşleşeni baştan atılır (`2a` sayfasında `2aK7` → `K`), sonra baştaki rakamlar (`3K1` → `K`). Harfi çıkarılamayan tag'lere (`PE`, `100`) dokunulmaz. |
| Değiştirme | `Renumbering.ChangeTagScheme`: `SetTagScheme` + yeni şemaya göre `Retag`'ler tek işlem; tek Ctrl+Z ile geri alınır. Harf şemasına dönüşte sayfa numarası düşer. `Renumbering.Tags` her zaman projenin şemasını kullanır. |
| Arayüz | `ake tag-scheme <proje> sayfa\|harf-sayi [--dry-run]`; `ake renumber --tags` ayarı izler. Editörde Düzen > Tag şeması (iki seçenek) ve Düzen > Tag'leri yeniden numarala. |

## Doğrulama

Referans makine `ake tag-scheme ... sayfa` ile çevrildi: 20 değişiklik (`K1 → 2K1`, `K2 → 4K1`,
`K0 → 5K1`), ERC temiz, `renumber --tags` ardından "Değişiklik yok". Sayfa 3 PNG olarak gözle kontrol
edildi (`-3F1`, `-3A4`, notlarda `-2K1:14 / 2.1`). Geri dönüş ve geri alma test edildi.

## Yapılmayanlar

- Sembol taşındığında ya da sayfa numarası değiştiğinde tag kendiliğinden güncellenmez; yeniden
  numaralandırma elle çalıştırılır.
- Klemenslerin `X8-1.1` biçimindeki uç numaraları (klemens adı) bu şemanın dışındadır.
- Patch formatında şema değiştirme işlemi yok (yalnızca CLI ve editör).
- Kolon bazlı numara (`3K1` yerine sayfa + kolon) seçeneği yok.
