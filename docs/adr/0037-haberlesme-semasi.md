# ADR-0037: Haberleşme şeması (bus topolojisi) ve cihaz ağ adresi

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0033 (uç özellikleri), ADR-0036 (bağlantı türleri), ROADMAP Faz 6b-5; Faz 7 (PLC bus topolojisi)

## Bağlam

Kullanıcı, haberleşme uçlarını belirttiğinde haberleşme şemasının otomatik çıkmasını istedi (örnek: Profinet, Ethernet ve DriveCLiQ
kablolarının renkli gösterildiği, cihazların IP adresli ve yapı gruplarıyla çerçevelendiği bir bus topolojisi sayfası).

## Karar

| Konu | Karar |
|---|---|
| Girdi | Uçları `Haberleşme` türünde (ADR-0033) olan iki farklı cihaz arasındaki bağlantılar; ya da haberleşme kategorisinde bir bağlantı türü (ADR-0036) atanmış bağlantılar. Haberleşme bağlantısı yoksa `haberlesme.yok` hatası ne yapılacağını söyler. |
| Cihaz ağ adresi | `Device.Address` (IP gibi, boşluksuz, en çok 64 karakter), `SetDeviceAddress` komutu, dosyada `address` (boşsa yazılmaz), cihaz özellikleri penceresinde "Ağ adresi (IP)". |
| Sayfa | Haberleşme şeması projede **gerçek bir sayfadır** (`PageKind.CommunicationDiagram`): sayfa listesinde görünür, PDF çıktısına ve içindekilere girer. Çerçeve, ızgara ve **antet** normal şablondan gelir (proje adı, sayfa başlığı, sayfa sayısı…). İçerik projeden her çizimde otomatik üretilir; kaydedilmez. Editör: **Bağlantı > Şemalar > Haberleşme şeması sayfası** sayfayı ekler (şablon yoksa varsayılan şablonu da) ya da varsa açar. |
| Üretim | `CommunicationDiagram` (Ake.Render): şemayı şablonun **çizim alanına sığdırır**; sığmıyorsa küçültüp yeniden yerleştirir (çok küçülürse uyarı verir). `Build(project)` aynı sayfayı sahne olarak verir (komut satırı ve testler). |
| Yerleşim | Cihazlar bulundukları yere (+SC1 …) göre **bantlara** ayrılır (yeri olmayanlar "Genel", en sonda); bant başına kutular soldan sağa, sığmayan sonraki satıra. Kutuda `-tag`, açıklama (ya da tip) ve **kırmızı ağ adresi**. Uç adları kutunun alt kenarında (üst satıra giden uç, alt satırdaki kutuda üst kenarda). |
| Kablolar | Kablonun türünün **rengi ve çizgi biçimiyle**; kablo adı (tel numarası ya da `-W1:2`) çerçeveli etiket olarak. **Hiçbir kablo kutunun içinden geçmez** (testle doğrulanır): iki ucun kanalı aynı satır aralığındaysa o aralıktaki kendi izinden, değilse kutular arası **koridordan** dikey inilir; her kablonun aralıkta ve koridorda kendi şeridi vardır. Böylece çizim elle düzeltme gerektirmez. Sağ üstte kullanılan türlerin **gösterge**si. |
| Çıktı | CLI: `ake haberlesme <proje> --cikti x.pdf|png|svg`. Örnek: `examples/haberlesme/bus-topolojisi.json`. |
| Ek | `TextItem.Color` (isteğe bağlı): PDF, PNG ve SVG'de yazı rengi (IP adresi kırmızı). |

## Yapılmayanlar

- Sayfanın içeriği elle düzenlenemez (her çizimde üretilir; sayfaya serbest grafik eklenebilir). Cihazları elle yerleştirme ayrı bir iş.
- Kablo-kablo kesişimleri azaltılmaya çalışılmaz (kablolar kutulardan geçmez ama birbirini kesebilir).
- Şemada ağ adresi dışında bilgi (alt ağ maskesi, VLAN, hız) yok; protokol (Profinet/EtherCAT…) bağlantı türünün adından okunur.
- Faz 7 bus topolojisi (PLC düğümleri, EK1100/EL… zinciri) bu şemaya henüz bağlı değil.
