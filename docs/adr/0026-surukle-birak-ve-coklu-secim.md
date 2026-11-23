# ADR-0026: Sürükle-bırak, çoklu seçim, kopyala/yapıştır ve tel çekme

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-17
- **İlişki:** ADR-0012 (editörde düzenleme), ADR-0024 (paneller), ADR-0025 (makrolar)

## Bağlam

Editörde yalnızca tek sembol seçilip taşınabiliyordu; yeni cihaz eklemek, tel çekmek, silmek ya da
kopyalamak mümkün değildi. Proje sahibi sürükle-bırakı istedi; yaygın E-CAD alışkanlıkları esas alındı.

## Kararlar

| Konu | Karar |
|---|---|
| Fare | Sol tık sembolü seçer; **Ctrl+tık** seçime ekler/çıkarır. Seçili sembolü sürüklemek **bütün seçimi** taşır (ızgaraya yakalanır, kesikli hayaletler gösterilir). Boş alanda sol tuşla sürüklemek **seçim dikdörtgeni** çizer: kutusu tamamen içinde kalan semboller seçilir (Ctrl ile eklenir). **Orta ya da sağ tuş** görünümü kaydırır, tekerlek yakınlaştırır. |
| Toplu düzenleme | `Ake.Core.Editing.Selection`: taşıma, döndürme (her sembol kendi ucu çevresinde), silme; her biri **tek işlem, tek Ctrl+Z**. |
| Silme | Seçilen semboller kalkar (`RemovePlacement`, yeni command). Sayfalarda **hiç sembolü kalmayan cihaz** bağlantıları ve potansiyel işaretleriyle birlikte silinir; sembolü kalan cihazın function'ı yerleşimsiz kalır, bağlantıları durur. PLC ağacının parçası olan cihaz silinmez, yalnızca sembolleri kalkar. |
| Kopyala/yapıştır | Ctrl+C seçimi makro olarak panoya alır (ADR-0025 kuralları); Ctrl+V **yeni kimlik ve tag'lerle** imlecin son durduğu yere yapıştırır, yapıştırılanlar seçili kalır. Ctrl+X kopyalayıp siler. Pano uygulama içidir (oturum ömrü). |
| Navigator'dan sürükle-bırak | Cihazlar navigator'ındaki satır sayfaya bırakılınca cihazın **yerleştirilmemiş ilk function'ı** bırakılan noktaya yerleşir; hepsi yerleşikse neden söylenir. |
| Kütüphaneden sürükle-bırak | Semboller panelindeki sembol bırakılınca **yeni cihaz** oluşur: uçları sembolün uçları kadar ("1", "2"…), sembol projeye kopyalanır, tag harfi sembol adından tahmin edilir (lamba P, motor M, kontaktör/röle K, sigorta/şalter F, buton S, klemens X, besleme T, diğerleri A), numarası o harfte bir sonraki. Tek cihazlık makro olarak yerleştirildiği için tag, yapı düğümü ve sembol kopyası makro kurallarıyla aynıdır. |
| Dosya bırakma | Gezgin'den sürüklenen `*.makro.json` bırakılan noktaya yerleşir. |
| Tel çekme | Bağlantı > Tel çek (W): ilk uca, sonra ikinci uca tıklanır; kesikli önizleme imleci izler; mod açık kalır, Esc ya da W bitirir. Uca yakalama 2,5 mm. Yeni tel numarasızdır (Tel numaraları komutu verir). Geçersiz bağlantıyı (aynı uç vb.) model reddeder. |
| Komutlar | Başlangıç > Pano (Yapıştır, Kes, Kopyala), Düzen'e Sil (Delete), Seçim'e Tümünü seç (Ctrl+A). |
| Sürükle-bırak API'si | Avalonia 12 `DataTransfer` / `DoDragDropAsync`, uygulama içi veri biçimi `aktapokus-electric/surukle`. |

## Doğrulama

Oturum testleri (`DragAndSelectionTests`) ve çekirdek testleri (`SelectionTests`): pencere seçimi,
tek adımda geri alınan taşıma/döndürme/silme, yeni tag'lerle yapıştırma, uca yakalama ve tel, yerleşimsiz
function'ı bırakma, kütüphane sembolünden cihaz. Başsız görüntü aracıyla çoklu seçim vurgusu ve şerit
gözle kontrol edildi. **Fareyle sürükleme, dikdörtgen seçimi ve tel çekme gerçek pencerede denenmedi**
(masaüstünde fare kullanılmıyor); davranış oturum testleriyle doğrulandı.

## Yapılmayanlar

- Tel çekerken köşe noktası verme ve teli sayfada elle yönlendirme (tel yolu otomatik, ADR-0011).
- Telin seçilip silinmesi (şimdilik yalnızca sembol silinir; bağlantı sembolle gider).
- Sürüklerken sembolün kendisinin (yalnızca kutusu değil) önizlemesi; yapıştırırken imlece yapışık önizleme.
- Sayfalar arası sürükle-bırak ve kopyala/yapıştırda başka projeye taşıma (pano uygulama içi).
