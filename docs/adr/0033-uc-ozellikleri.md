# ADR-0033: Uç özellikleri (tür, bağlantı tipi, kontak türü) ve sembolde cihaz adı

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0003 (veri modeli), ADR-0016 (sembol içe aktarma), ADR-0023 (şerit), ROADMAP Faz 6b-1

## Bağlam

Bir bağlantı noktasının yalnızca adı vardı. Standart bir E-CAD'de uçların rolü (güç, sinyal, haberleşme), fiziksel bağlantısı
(vidalı, M8, M12, RJ45) ve kontak türü (NO, NC) de tutulur; blok editöründe de verilebilir. Sembole cihaz adı ve tag harfi de
konabilmeli. Haberleşme şeması, tek hat ve ürün kütüphanesi bu bilgiye dayanacak.

## Karar

| Konu | Karar |
|---|---|
| Özellikler | `PointAttributes`: `Kind` (güç, sinyal, haberleşme, PE, ekran), `Connector` (vidalı, push-in, faston, M8, M12, RJ45, D-Sub, diğer), `Contact` (NO, NC, değiştirmeli), `ConnectorCode` (serbest metin, en çok 40 karakter, örn. "A-kod 4 pin"). Hepsi isteğe bağlıdır; "belirtilmemiş" varsayılandır. |
| Nerede | `ConnectionPoint.Attributes` (projedeki nokta) ve `SymbolPin.Attributes` + `SymbolPin.Name` (sembol ucu). `SymbolDefinition` ayrıca `DeviceName` (varsayılan cihaz açıklaması) ve `TagLetter` (1–3 büyük harf) taşır. |
| Komut | `SetPointAttributes`. Geri alınır; adlara ve bağlantılara dokunmaz. Bilinmeyen değer ve 40 karakterden uzun kod reddedilir. |
| Sembolden cihaza | Kütüphane sembolü sayfaya bırakılınca cihazın uç adları sembol ucunun adından (yoksa sıra numarasından), özellikleri sembol ucundan, tag harfi `TagLetter`'dan (yoksa addan tahminle), açıklaması `DeviceName`'den (yoksa sembol adından) gelir. `AssignSymbol` yalnızca henüz özelliği olmayan noktalara özellik verir; adlara dokunmaz. Makro kopyası özellikleri taşır. |
| Dosya | Alanlar isteğe bağlıdır ve belirtilmemişse yazılmaz: eski dosyalar aynen açılır, özelliksiz projenin JSON'u değişmez. Şema numarası artmaz. Sembol dosyası, proje ve kütüphane aynı DTO'yu kullanır. |
| Çizim | Kontak türü uç adının yanına `NO`, `NC` ya da `CO` olarak yazılır. Bağlantı tipi ve tür yalnızca özelliklerde görünür. |
| Editör | Blok editörü: sağda seçili uç paneli (ad, tür, bağlantı, kontak, kod), altta cihaz adı ve tag harfi. Uç adları tekil, boşluksuz ve `:` içermez. Cihaz özellikleri penceresinin "Noktalar" sekmesi her nokta için aynı beş alanı düzenler. |

## Yapılmayanlar

- Haberleşme protokolü (EtherCAT, Profinet…) ve konektör kodu için seçim listesi; şimdilik tür `Haberleşme` + serbest kod. Haberleşme
  şeması işinde ele alınır.
- QElectroTech ve DXF içe aktarma bu alanları doldurmaz.
- Özelliklere göre ERC kuralı (örn. M12 ile vidalı uç bağlantısı uyarısı) yok.
