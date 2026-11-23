# ADR-0032: Tel kavşağı (T bağlantısı)

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0003 (veri modeli), ADR-0029 (tel köşe noktaları), ADR-0030 (klemens şeridi)

## Bağlam

Bir tel iki bağlantı ucunu bağlar. Bir telin ortasından ikinci bir tel ayırmak (T-kavşağı), bir noktada üç ya da dört telin
buluşması mümkün değildi; kullanıcı, tel çekmeyi denerken bunu eksik olarak bildirdi.

## Karar

| Konu | Karar |
|---|---|
| Model | Kavşak, tek bağlantı noktalı (`1`), `FunctionKind.Junction` function'lı ve `DeviceKind.Junction` device'tır; sayfada normal bir yerleşimle durur. Yeni bir nesne türü, yeni dosya alanı ya da şema değişikliği yoktur. |
| Neden device | Bağlantı noktasını bulma, net/potansiyel hesabı, silme, taşıma, geri alma ve JSON'a yazma zaten device üzerine kurulu; kavşak hepsini bedavaya alır. Aynı noktaya bağlı teller aynı net'tedir, tel numaralandırma ve ERC doğru çalışır. |
| Tag | `J1`, `J2`… (kullanılmayan en küçük numara). Tag, çizimde yazılmaz; uç adı da yazılmaz. |
| Komutlar | `Junctions.Create` ve `Junctions.Split` (Core): `Transaction` döndürür, tek adımda geri alınır. `Split`, bağlantıyı kaldırır, kavşağı ekler ve `baş – kavşak` ile `kavşak – son` bağlantılarını kurar. İlk yarı eski teli korur; ikinci yarı aynı numara, kesit ve renkte yeni tel alır. Köşe noktaları atılır (iki yarı yeniden otomatik yönlendirilir). |
| Kablo damarı | Damara atanmış bağlantı bölünmez (`kavsak.damar`); bir damar iki bağlantıya atanamaz. |
| Silme | Kavşağı silmek (`RemoveDevice`) ona bağlı bütün telleri de siler. Tellerin birleştirilerek kavşağın kaldırılması ayrı bir iş (yapılmadı). |
| Çizim | Dolu küçük nokta (yarıçap 0,9 mm); ucun yönü yoktur, teller her yönden gelir. |
| Listeler | Kavşak malzeme listesine, cihaz navigator'ına ve cihaz sayısına girmez. ERC'de yeni bulgu üretmez. |
| Editör | Tel çek modunda bir telin üzerine tıklamak orada kavşak koyar: telin tıklanan yere en yakın noktası (yatay parçada x, dikey parçada y ızgaraya yakalanır) kavşak olur. İlk tıklamaysa kavşak telin başlangıcı olur, ikinci tıklamaysa tel orada biter. Yeniden yönlendirmede uçlar sabittir, kavşak konmaz. Yerleşim koordinatı çizim alanına göredir. |

## Yapılmayanlar

- Makro yakalama ve yerleştirmede kavşakların davranışı ayrıca sınanmadı.
- Kavşağı kaldırıp iki teli birleştirmek; kavşak taşınınca telin köşe noktalarını güncellemek.
- Otomatik yerleşim (`ake` AutoLayout) kavşağı yerleştirmez.
