# ADR-0034: Çok katlı klemens

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0003 (veri modeli), ADR-0030 (klemens şeridi düzenleyici), ROADMAP Faz 6b-2

## Bağlam

2 ve 3 katlı klemensler (üst üste duran, birbirinden elektriksel olarak ayrı katlar) panolarda yaygındır. Modelde yalnızca tek
katlı klemens vardı (ADR-0030 "Yapılmayanlar").

## Karar

| Konu | Karar |
|---|---|
| Model | Her kat, şeritte ayrı bir `Terminal` function'ıdır; aynı **adı** taşır. `Function.Level` (1–3, varsayılan 1) katı söyler. Katlar elektriksel olarak ayrıdır (aralarında köprü yoksa). Böylece net, potansiyel, ERC, klemens planı ve çizim değişmeden çalışır. |
| Nokta adları | 1. kat `ad.a` / `ad.b` (değişmedi); 2. ve 3. kat `ad.a2` / `ad.b2`, `ad.a3` / `ad.b3`. Üretici: `TerminalNames.Point`. |
| Ad kuralı | Ad şeritte artık **(ad, kat)** çiftiyle tekildir. Aynı ad farklı katta olabilir (`terminal.ad_tekrar` yalnızca aynı katta). |
| Komutlar | `AddTerminal.Create(ad, kat)` ile eklenir; `SetTerminalLevel` mevcut klemensin katını değiştirir ve a/b noktalarını yeniden adlandırır (aynı katta aynı ad varsa reddedilir). Kat 1–3 dışında `terminal.kat_gecersiz`. |
| Doğrulama | Kat yalnızca klemens function'ında 1'den büyük olabilir (`function.kat_gecersiz`; `AddDevice` ve dosya yükleme). |
| Dosya | `level` yalnızca 2 ya da 3 ise yazılır; eski dosyalar ve tek katlı projeler değişmez. Şema numarası artmaz. |
| Rapor | Klemens planında 2. ve 3. kat `ad (2. kat)` biçiminde görünür; 1. kat aynı kalır (snapshot değişmedi). |
| Editör | Klemens şeridi penceresinde her satırda **Kat** (1/2/3) seçimi; yinelenen (ad, kat) kırmızı çerçeve alır. Yeni satır 1. kat gelir. |

## Yapılmayanlar

- **Çok bağlantılı klemens** (kat başına ikiden çok bağlantı noktası), **PE klemensi** özel türü ve **sonlandırıcı/ayırıcı levha**.
- Katların şemada üst üste tek sembol olarak çizilmesi: her kat kendi sembolüyle ayrı yerleştirilir.
- Katlar arası köprü bayrağı: köprü bayrağı komşu satırlar içindir; farklı katlar arasındaki köprü tel çekerek kurulur.
