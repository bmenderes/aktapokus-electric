# ADR-0039: Tek hat şeması ve güç zinciri

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0003 (model), ADR-0037 (otomatik üretilen sayfalar), ROADMAP Faz 6b-6

## Bağlam

Kullanıcı tek hat şemasının güç dağıtımından otomatik çıkmasını istedi. Kapsam kararı: **yalnızca güç hattı** (kaynak → koruma → anahtarlama → yük);
kumanda devreleri tek hatta girmez.

## Karar

| Konu | Karar |
|---|---|
| Güç zinciri | `PowerChain.Build(project)` (Ake.Core, model analizi): beslemeden yüklere inen **ağaç** (`PowerNode`). Başlangıç: faz potansiyel işaretinin konduğu cihaz (adı en küçük faz, örn. L1); faz yoksa DC artı. İki uçlu `Protection` ve `PowerContact` function'ları zincir elemanıdır (çok kutuplu cihaz tek düğüm); klemensler şeffaf geçilir; `Motor` ve `PowerSupply` yüktür. Bobin, yardımcı kontak, PLC kanalı gibi kumanda elemanları zincire girmez. Aynı cihaz bir yolda iki kez gezilmez. |
| Düğüm bilgisi | Cihaz, function, rol (Besleme, Koruma, Anahtarlama, Yük), anma akımı (function'ın `RatedCurrentA`), kendisine gelen telin kesiti, kendisine gelen bağlantı kablo damarıysa kablonun adı ve tipi, besleme için potansiyel adları. |
| Sayfa | Tek hat şeması projede **gerçek bir sayfadır** (`PageKind.SingleLineDiagram`): sayfa listesinde görünür, PDF çıktısına girer; çerçeve, ızgara ve **antet** şablondan gelir; içerik her çizimde zincirden üretilir. Editör: **Bağlantı > Şemalar > Tek hat şeması sayfası**; varsa açar, yoksa ekler (şablon yoksa varsayılanı da). Güç zinciri yoksa eklenmez (`tekhat.zincir_yok`). |
| Çizim | `SingleLineDiagram` (Ake.Render): ağaç yukarıdan aşağıya; her eleman kutu (sembol, `-tag`, açıklama en çok iki satır, anma akımı ya da rol); kol başına bir yük, ortak parçalar (ana şalter) tek kez; kenarlarda tel kesiti ve kablo. Çizim alanına sığdırılır (çok küçülürse uyarı). Sayılar Türkçe yazılır (2,5 mm², 6,3 A). |
| Çıktı | CLI: `ake tekhat <proje> --cikti x.pdf|png|svg`. Örnek: referans makine (`examples/ref-machine`). |

## Yapılmayanlar

- Üç fazın ayrı gösterimi (tek hatta tek çizgi), kutup sayısı, gerilim ve güç (kW) yazısı: modelde motor gücü ve besleme gerilimi yok.
- Birden çok faz kaynağı arasında dönüşüm (trafo ile ikinci gerilim) ve DC dağıtım kolu ayrı bir zincir olarak (yalnızca fazsız projede DC başlar).
- Kablo uzunluğu, gerilim düşümü, kısa devre hesabı.
- Sayfanın elle düzenlenmesi.
