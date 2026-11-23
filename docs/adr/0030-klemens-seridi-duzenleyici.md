# ADR-0030: Klemens şeridi düzenleyici

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0003 (veri modeli), ADR-0004 (command), ADR-0006 (raporlar: klemens planı)

## Bağlam

Klemens şeridi, elektrik projelerinin en çok elle düzenlenen parçasıdır: klemensler eklenir, sıralanır, yeniden numaralanır,
komşu klemenslere köprü atılır. Model bunları zaten taşıyor ama editörde yalnızca tek tek tel çekilebiliyordu.

## Model (değişmedi)

- Şerit: `DeviceKind.TerminalStrip`. Klemens: şeridin `Terminal` türünde, `PassThrough` bir function'ı; **adı** `Description`,
  **iç** noktası `ad.a`, **dış** noktası `ad.b`. Şeridin function sırası klemens sırasıdır (klemens planı bu sırayla yazar).
- Köprü: aynı şeritteki iki klemens ucu arasında **tel ya da damarı olmayan** bağlantı. Klemens planı bunu "Köprü" sütununda gösterir.

## Karar

| Konu | Karar |
|---|---|
| Komutlar | `AddTerminal` (verilen sıraya, hazır function ile), `RemoveTerminal`, `MoveTerminal`. Yeniden adlandırma mevcut `SetFunctionDescription` ve `RenameConnectionPoint` ile yapılır; köprü mevcut `Connect`/`Disconnect` ile. Yeni komut yalnızca model yapısını değiştirenler içindir. |
| Ad kuralı | Boş olamaz, boşluk ya da `:` içeremez, şeritte tekil olmalı. Adı değiştirmek `a`/`b` noktalarını da `yeniad.a`, `yeniad.b` yapar. |
| Kaldırma | Tel ya da kablo damarı bağlı klemens **kaldırılmaz** (`terminal.bagli`: önce bağlantı kaldırılır). Köprüleri, yerleşimleri, potansiyel işaretleri ve PLC kanalı ataması birlikte gider; geri almak hepsini getirir. Şeritte en az bir klemens kalır. |
| Pencere | Editörün kendi penceresi (Ekle > Klemens > Klemens şeridi; sağ tık menüsü; Klemensler listesinde şerit satırı seçiliyken): sıra okları, ad kutusu, iç/dış köprü onay kutuları, kaldır, "Klemens ekle", "Yeniden numarala" (1…n). Hatalı ya da yinelenen ad kırmızı çerçeve alır, Tamam kapanır. |
| Uygulama | **Tamam tek işlemdir**: sırayla kaldırmalar, yeniden adlandırmalar (yeni klemensin adı eskinin adı olabilsin diye eklemeden önce), eklemeler (sona), taşımalar (istenen sıraya), köprüler. Biri reddedilirse hiçbiri uygulanmaz; tek geri alma adımıdır. |
| Köprü bayrağı | Satırdaki iç/dış işareti, o klemensin **listedeki bir sonraki** klemensle aynı noktada köprülü olduğunu söyler. İşaretli ve köprü yoksa kurulur. İşaretsiz ve köprü varsa **yalnızca pencere o köprüyü gösteriyorsa** (çift daha önce de komşuydu ve köprülüydü) kaldırılır. Komşu olmayan klemensler arasındaki köprülere dokunulmaz (ör. 1–3 arası köprü, 2 kaldırılınca korunur). |
| Yeni klemens ve sayfa | Yeni klemens sayfaya yerleştirilmez (Cihazlar listesinde "yerleştirilmemiş" görünür; sürükle-bırak ya da otomatik yerleşim). |

## Yapılmayanlar

- **Üst/alt kat** (çok katlı klemens) ve **sonlandırıcı/ayırıcı levha**: modelde karşılığı yok; ürün ve çizim kararı ister.
- Köprünün ürün olarak (köprü tarağı, klemens sayısı) ele alınması: köprü şimdilik yalnızca bağlantıdır.
- Birden çok şeridi aynı pencerede düzenlemek; şeritler arası klemens taşımak.
- Yeniden numaralamada akıllı davranış (örn. L1/L2/N adlarını korumak): düğme hepsini 1…n yapar; kullanıcı eylemidir ve geri alınır.
