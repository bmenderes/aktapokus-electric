# ADR-0025: Makrolar

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-17
- **İlişki:** ADR-0004 (command/patch), ADR-0021 (tag şeması), Faz 6 planı (kapsam listesi: makrolar)

## Bağlam

Yaygın E-CAD iş akışının temeli makrodur: bir kez çizilmiş devre parçası (motor çıkışı, besleme, sinyal
lambası…) kaydedilir ve başka sayfalara/projelere yeni tag'lerle yerleştirilir. Proje sahibi kendi
makrosunu oluşturmayı istedi; üretici makroları (DXF/DWG) içe aktarıcı yazılınca aynı makro biçimine
çevrilecek.

## Kararlar

| Konu | Karar |
|---|---|
| Model | `Ake.Core.Macros.Macro`: cihazlar (function ve bağlantı noktalarıyla), aralarındaki bağlantılar, sembollerin makronun sol üst köşesine göre yerleri, kullandıkları kullanıcı sembolleri, ad ve açıklama. Cihazlar yapı düğümü (=, +) taşımaz. `Check()` bağlantının ya da yerleşimin makro dışına gitmesini reddeder. |
| Yakalama | `MacroService.Capture(proje, sayfa, ad, alan?)`: alan verilmezse sayfanın tamamı (sayfa makrosu), verilirse ana görünümü dikdörtgende duran cihazlar (pencere makrosu). Cihaz bütün function'larıyla gelir (bobin sayfadaysa başka sayfadaki kontağı da); **klemens şeridinden yalnızca yerleşik klemensler** alınır. İki ucu da makroda olan bağlantılar alınır; **tel numarası boşaltılır**, kesit ve renk kalır, kablo damarı bağı düşer. Dışarıya giden bağlantılar alınmaz; yerleştirmeden sonra bağlanır (ERC açık uç uyarısı verir). |
| Yerleştirme | `MacroService.Insert(proje, makro, sayfa, x, y)` tek `Transaction` üretir: projede olmayan kullanıcı sembolleri, cihazlar, bağlantılar, yerleşimler; **tek Ctrl+Z** ile geri alınır, audit log'a tek kayıt düşer. Bütün kimlikler yenidir. Cihazlar hedef sayfanın yapı düğümlerini alır. |
| Tag'ler | Harf makrodaki tag'den okunur; numara hedef kapsamda o harfteki **en büyük numaranın bir fazlası** (boşluk doldurulmaz). Sayfa şemasında (ADR-0021) sayfa numarasıyla başlar (`4K2`). Harfi okunamayan tag (PE) korunur. |
| Konum | Verilmezse sayfadaki sembollerin en solu hizasında, en alttakinin 40 mm altı; boş sayfada (30, 40). Editörde ızgaraya yakalanır. |
| Dosya | `*.makro.json`, `macroVersion` 1. Cihaz, bağlantı ve sembol öğeleri proje dosyasıyla aynıdır; okuma projeyle aynı katılıkta (bilinmeyen alan ve daha yeni sürüm reddedilir). Örnek: `examples/macros/motor-cikisi.makro.json`. |
| CLI | `ake macro create <proje> <sayfa> --out <dosya> [--alan x,y,g,y] [--ad ad]`, `ake macro insert <proje> <makro> <sayfa> [--x] [--y] [--dry-run]`. |
| Editör | Ekle > Makro: **Makro ekle** (Ctrl+M; dosya seçilir, etkin sayfaya yerleşir), **Sayfadan makro**, **Seçimden makro** (seçili cihazın o sayfadaki görünümleri); ikisi kaydetme penceresi açar. |

## Doğrulama

Referans makinenin 2. sayfası (Motor 1) makro yapıldı (4 cihaz, 9 bağlantı, 14 sembol) ve 4. sayfaya
yerleştirildi: `-F3`, `-K3`, ayna tablosu ve `(4.7)` referansı doğru çizildi, tekrar eden tag yok;
ERC yalnızca beklenen açık uç uyarılarını verdi. Testler: `MacroTests` (Core), `MacroJsonTests`,
editör oturumu ve CLI testleri. Editörde dosya seçme/kaydetme pencereleri gerçek pencerede denenmedi.

## Yapılmayanlar

- Makro kütüphanesi (klasör, önizleme, arama) ve sürükle-bırakla yerleştirme; yerleştirme konumu fareyle seçilmiyor (sonra taşınır; çoklu taşıma Faz 6.2).
- Değer kümeleri (aynı makronun 4 kW / 7,5 kW gibi varyantları) ve makro yer tutucuları.
- Potansiyel işaretleri (24VDC gibi) ve kablolar makroya girmez.
- Dikdörtgenle seçim yok; pencere makrosu şimdilik CLI'de `--alan` ile ya da editörde tek cihaz seçimiyle.
