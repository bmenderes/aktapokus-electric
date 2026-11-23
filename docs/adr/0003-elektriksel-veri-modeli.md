# ADR-0003: Elektriksel veri modeli

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15
- **Kaynak:** `docs/faz-1-veri-modeli.md` içindeki 14 açık soru, önerildiği gibi onaylandı

## Bağlam

Model, projenin tek doğruluk kaynağıdır (ADR-0001); editör, raporlar, PLC köprüsü, pano ve AI
onun üzerine kurulur. Burada verilen kararları sonradan değiştirmek bütün bu katmanları ve
kaydedilmiş proje dosyalarını etkiler.

## Kararlar

### Device, Function, bağlantı

| Konu | Karar |
|---|---|
| Device / Function | Device fiziksel üründür (BOM'da sayılır), Function onun elektriksel parçasıdır (şemada çizilir ve bağlanır). Bağlantı noktaları Function'a aittir. |
| Klemens | Ayrı sınıf yoktur. Klemens grubu `Kind = TerminalStrip` olan Device'dır; her klemens `PassThrough = true` olan bir Function'dır. Köprü, klemensler arası tel atanmamış bir Connection'dır. |
| Connection ortamı | Ya `Wire`, ya bir kablo damarı, ya da hiçbiri. İkisi birden olamaz; bir damar tek bağlantıya atanır. |
| Net / Potential | İkisi ayrı kavramdır ve saklanmaz, hesaplanır. Net tellerle doğrudan bağlı noktalardır; Potential geçirgen function'lar üzerinden birleşen net'lerdir. |
| Potansiyeli bölenler | Kontak, sigorta, şalter potansiyeli böler. Yalnızca `PassThrough` function'lar geçirgendir. |
| Potansiyel adı | Bağlantı noktasına konan `PotentialSource` işaretinden gelir, bağlantıların üzerine yazılmaz. Farklı adlı işaretler aynı potansiyele düşerse `Potential.HasConflict` doğru olur; hata olarak raporlamak ERC'nin işidir. |
| Function türleri | Sabit enum (`FunctionKind`). Kütüphane geldiğinde yetersiz kalırsa şablona taşınır. |

### Yapı ve tag

| Konu | Karar |
|---|---|
| Aspect'ler | `=` ve `+` aspect'leri `StructureNode` ağacıdır; `-` aspect'i Device'ın `ProductTag` alanıdır. |
| Tam tag | Saklanmaz, yapı yolu ve ürün tag'inden üretilir (`=A1.M1+P1-K1:A1`). İç içe düğümler nokta ile ayrılır. |
| Kimlik | Her nesnenin tipli, kararlı bir kimliği vardır (`DeviceId`, `FunctionId`…); yeni kimlikler GUID v7'dir. Tag kimlik değildir ve değişebilir. Kimlikler proje genelinde tekildir. |

### PLC

| Konu | Karar |
|---|---|
| Topoloji | Controller → Network → Node → Module → Channel → ProcessDataEntry. Rack/slot değil bus topolojisi. |
| Üretici bağımsızlığı | Model hiçbir PLC üreticisine bağlı değildir. Beckhoff (EtherCAT coupler + terminal), Rockwell (şasi + slot, EtherNet/IP uzak I/O) ve Schneider (gövde + genişleme modülü, Modbus TCP / CANopen / Sercos) aynı ağaçla temsil edilir. Şasi ve genişleme veri yolu `NetworkKind.LocalBus` ağıdır. |
| Device ilişkisi | PLC ağacı Device'ların üzerine ikinci bir görünümdür. Controller, Node ve Module fiziksel ürüne `DeviceId` ile bağlanır; kanalın elektriksel ucu o Device'ın bir Function'ıdır. |
| Adres | `Channel.Address` serbest metindir; biçimi platforma göre değişir (`%IX0.3`, `Local:2:O.Data.3`). |

### Grafik temsil

| Konu | Karar |
|---|---|
| Placement | Yalnızca Function'ı gösterir; modelden bağımsızdır. Bir function'ın en çok bir ana, istenildiği kadar referans görünümü olur; hiç yerleştirilmemiş function geçerlidir. |
| Tel geometrisi | Modelde yoktur. Connection mantıksal bağdır; çizim yolu Faz 5'te ayrı grafik nesne olur. |
| Sayfa | Faz 1'de yalnızca kimlik, numara, başlık ve yapı düğümü. |

### Değişiklik ve dosya

| Konu | Karar |
|---|---|
| Değiştirilebilirlik | Model sınıfları değiştirilebilir, ama değiştiren üyeler `internal`dir. Core dışından yalnızca okunur. |
| Yükleme | `Project.Create` modeli bütün olarak kurar. Bu, "model yalnızca Command'larla değişir" kuralının tek istisnasıdır ve audit log'a yazılmaz. |
| Doğrulama sınırı | `Project.Create` yalnızca iç tutarlılığı denetler: tekil kimlik, çözülebilir referans, döngüsüz yapı ağacı. Mühendislik kuralları (açık uç, tekrarlanan tag, kısa devre) ERC'dedir (Faz 2). |
| Dosya | Tek JSON dosyası. Şema versiyonu dosyada durur, modelde değil. |
| Canonical biçim | Alan sırası sabit, liste sırası modeldeki sıra, satır sonu LF, UTF-8 BOM'suz, boş isteğe bağlı alanlar yazılmaz. Aynı model her zaman aynı metni üretir. |
| Katı okuma | Bilinmeyen alan, eksik zorunlu alan ve bilinmeyen enum değeri hatadır; sessizce atlanmaz. |
| Şema versiyonu | Asıl okumadan önce denetlenir; daha yeni dosya `proje.sema_yeni` ile reddedilir. |
| Dosya şeması ayrı | Dosya biçimi (`Ake.Serialization` içindeki DTO'lar) model sınıflarından ayrıdır; model değişse de dosya ancak şema versiyonu artırılarak değişir. |
| Kod dili | Tip ve üye adları İngilizce; belgeler ve hata mesajları Türkçe. |

## Taslaktan farklar

- Kablo damarı sınıfının adı `Core` değil `CableCore`: `Ake.Core` ad alanıyla çakışıyordu.
- `SchemaVersion`, `Project` sınıfında değil dosya şemasındadır.
- `NetworkKind` değerlerine `LocalBus`, `ModbusTcp` ve `CanOpen` eklendi (üretici bağımsızlığı).

## Sonuçlar

- Tel numaralandırma net'e, kısa devre denetimi ve sayfalar arası potansiyel oku potansiyele bakar.
- Çok katlı klemens ve iç/dış taraf bilgisi şimdilik bağlantı noktası adında taşınır (`1.a`, `1.b`).
  Faz 3 terminal planında yetersiz kalırsa Function'a alan eklenir ve şema versiyonu artar.
- EtherCAT master'ının kendi portu modelde yoktur; ilk node master'a bağlı kabul edilir. Halka ve
  yedekli topoloji gerekirse Faz 7'de Controller'a port eklenir.
- GUID'ler elle yazılan JSON'u zahmetli kılar; karşılığında birleştirme ve import'ta çakışma olmaz.
- Liste sırası korunduğu için dosyada elle yapılan yeniden sıralama kalıcıdır; kaydetme sıralamaz.
- Şema versiyonu 1'dir. Göç (migration) altyapısı ilk şema değişikliğinde yazılacak.
