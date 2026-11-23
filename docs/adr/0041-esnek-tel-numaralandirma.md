# ADR-0041: Esnek tel numaralandırma

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0005 (numaralandırma), ROADMAP Faz 6c-1

## Bağlam

Tel numaralandırma "kaç numarasından başlanacağı ve hangi kodla" proje sahibi tarafından belirlenebilmeli; araya sayfa eklenince mevcut tel numaraları
değişmemeli ve numaralandırma kaldığı yerden sürmeli; baştan numaralandırma ise yalnızca istenirse yapılmalı. Eski yöntem her seferinde bütün telleri
1'den yeniden numaralıyordu.

## Karar

| Konu | Karar |
|---|---|
| Ayarlar | `WireNumberSettings` (proje ayarı): **kod/önek** (boşluksuz, en çok 12 karakter), **başlangıç**, **adım**, **basamak sayısı** (sıfırla doldurma) ve **şema** (potansiyel bazlı ya da sıralı). `"W"`, 100, 3 hane → `W100`, `W101`… Komut: `SetWireNumbering`; ayarı değiştirmek mevcut numaralara dokunmaz. |
| Yeni telleri numarala | `Renumbering.NewWires`: yalnızca **numarası olmayan** telleri numaralar; mevcut numaralara dokunmaz. Sıradaki numara, projenin kodunu taşıyan **en büyük** numaranın bir adım ötesidir (kodlu numara yoksa başlangıç); kodsuz ya da başka kodlu numaralar sayılmaz. Potansiyel bazlı şemada, numarası olan bir potansiyele bağlanan yeni tel o potansiyelin numarasını alır. İkinci çalıştırma bir şey yapmaz. |
| Baştan numarala | `Renumbering.Wires`: bütün telleri model sırasıyla ayarlara göre yeniden verir (yalnızca kullanıcı isterse). Tek adımda geri alınır. |
| Dosya | `wireNumbering`; varsayılansa yazılmaz, alanlar da yalnızca değişmişse. Şema numarası artmaz; eski dosyalar aynen açılır. |
| Editör | **Bağlantı > Numaralandırma**: *Yeni telleri numarala*, *Baştan numarala*, *Numaralandırma ayarları…* (kod, başlangıç, adım, basamak, şema ve örnek numaralar). |
| CLI | `ake renumber <proje> --wires [--new] [--prefix W --start 100 --step 1 --width 3] [--scheme …]`. Kod, başlangıç, adım ve basamak verilirse projenin ayarı olarak kaydedilir ve aynı adımda uygulanır; `--scheme` yalnızca o çalıştırma içindir. |

## Yapılmayanlar

- Sayfa tabanlı numaralar (`3.12`: sayfa + sıra) ve alt sayfalar (`3.1`): sayfa numaralandırma ve çapraz referans işiyle birlikte (ROADMAP Faz 6c-2).
- Kablo damarı numaraları (damar adı kablonun kendisidir).
- Numaranın tel/kablo etiketi çıktısına dökülmesi: ROADMAP Faz 6c-3.
