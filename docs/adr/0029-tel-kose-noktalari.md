# ADR-0029: Tel köşe noktaları

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0004 (command), ADR-0005 (proje dosyası), ADR-0025 (makrolar), ADR-0026 (tel çekme)

## Bağlam

Teller otomatik yönlendirilir (engellerden kaçan dik yol). Kullanıcı bazen telin belirli bir yerden geçmesini ister (okunurluk,
başka teli ya da sembolü dolaşmak, standart bir pano görünümü). Tel çekerken ve sonradan köşe noktası verilebilmeli; otomatik
yönlendirme her zaman geri dönülebilir bir seçenek olmalı.

## Karar

| Konu | Karar |
|---|---|
| Model | `Connection.Route`: `WirePoint(X, Y)` listesi, **sayfa koordinatı** (mm, y aşağı). Boşsa tel otomatik yönlendirilir. Çok sayfalı çizilen ("sayfa ucu notu" olan) tellerde anlamsızdır. Üst sınır 32 nokta, sonlu sayı. |
| Command | `SetWireRoute(bağlantı, noktalar)`: boş liste otomatik yönlendirmeye döndürür; geri alınır. `Connect` aynı bağlantıyla birlikte köşe noktası da alır (tek adım). |
| Çizim | Köşe noktalı tel otomatik yönlendiriciden geçmez: uçtan çıkış noktasına, sırayla köşe noktalarına, sonra öbür ucun çıkış noktası ve ucuna **dik parçalarla** gider. Aynı hizada olmayan iki nokta arasına dirsek konur; dirsek önceki parçanın yönünü sürdürecek şekilde seçilir (geri dönmek yerine ileri gitmek). Aynı kural tel çekerken önizlemede de kullanılır (`SheetRenderer.RoutePreview`). |
| Dosya | Bağlantıda `route`: tek metin `"x,y x,y…"` (grafik çoklu çizgisiyle aynı biçim). Alan isteğe bağlıdır; şema numarası değişmedi (şema 6 henüz yayımlanmamıştı). Bozuk metin yüklemeyi `connection.yol_gecersiz` ile reddeder. |
| Makro | Yakalarken köşe noktaları makronun sol üst köşesine göre tutulur; yerleştirirken yeni yere kayar. |
| Editör: tel çekme | W modunda ilk uca tıklanır; boş yerlere tıklamak köşe noktası verir (yapışma adımına, 5 mm, oturur; küçük daireyle işaretlenir); ikinci uca tıklamak bağlar. Önizleme uçtan köşe noktalarından geçip imlece (bir uca yakınsa o uca) gider. Backspace son köşe noktasını geri alır. |
| Editör: sonradan | Teli seçip sağ tık: **Köşe noktaları ver** (tel çekme gibi, uçlar sabit; telin öbür ucuna tıklamak bitirir; hiç nokta vermeden bitirmek otomatiğe döndürür) ve **Otomatik yönlendir**. Tek geri alma adımı. |

## Sonuçlar

- Köşe noktalı teller otomatik yönlendiriciye **bildirilmez**: öbür teller onların çevresinden dolaşmaz, kesişim noktası (düğüm
  noktası) daireleri çizilmez. Kullanıcı yolu elle verdiği için sorumluluk ondadır.
- Sembol taşınınca köşe noktaları yerinde kalır (mutlak koordinat); uçlar uzaklaşırsa tel uzar. Gerekirse teli yeniden yönlendirmek
  ya da otomatiğe döndürmek yeter.
- ERC ve potansiyel hesabı bağlantı kimliğiyle çalışır; köşe noktaları onları etkilemez.

## Yapılmayanlar

- Seçili telin parçasını ya da köşesini sürükleyerek taşıma (şimdilik yeniden yönlendirmek gerekir).
- Köşe noktalı telin düğüm noktası dairesi ve öbür tellerin ona göre yön bulması.
- Sembole göre göreli köşe noktaları (sembolle birlikte taşınma).
- Birden çok teli aynı anda yeniden yönlendirme.
