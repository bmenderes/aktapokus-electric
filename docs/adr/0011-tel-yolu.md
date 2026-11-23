# ADR-0011: Engel tanıyan tel yolu

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0009'daki "tel yolu engel tanımaz", "düğüm işareti yok", "notlar üst üste biner" ve
  "PLC kartı kanal başına ayrı kutu" kusurlarını giderir.

## Bağlam

ADR-0009'daki tel yolu üç parçalı düz bir yoldu ve çizimi yanlış okutuyordu: tel başka bir sembolün
içinden geçiyor, farklı net'lerin telleri üst üste binip tek tel gibi görünüyordu. Bir elektrik
şemasında bu, olmayan bir bağlantıyı var göstermek demektir; çıktı üretim belgesi olamıyordu.

## Kararlar

### Yol bulma

| Konu | Karar |
|---|---|
| Yöntem | Sayfa üzerinde ızgara ve A* araması (`WireRouter`). Teller yalnızca yatay ve dikey parçalardan oluşur. |
| Izgara | 2,5 mm adımlı düzenli çizgiler **ve** bütün uç koordinatları. Uçlar ızgaraya yuvarlanmaz, ızgara uçlara uydurulur; elle yerleştirilmiş, adım dışı semboller de tam ucundan bağlanır. Bir uca 1,2 mm'den yakın düzenli çizgi atılır. |
| Engeller | Her sembolün sayfadaki kutusu, PLC kart gövdeleri ve tag yazılarının kapladığı yer. |
| Sıra | Kısa teller önce yürütülür; uzunlar onların çevresinden dolaşır. Sıra ve sonuç deterministiktir. |
| Maliyet | Uzunluk + köşe cezası + başka teli kesme cezası. Aynı net'in var olan hattı üzerinde yürümek ucuzdur; böylece dağıtım telleri ortak bir gövde oluşturur. |
| Yol yoksa | Eski üç parçalı düz yol çizilir (engel tanımaz). Bu durum şu an raporlanmıyor. |

### Yanlış okumayı önleyen kurallar

1. Tel bir sembolün ya da kart gövdesinin içinden geçmez.
2. Farklı net'lerin telleri aynı hat üzerinde üst üste binmez.
3. Farklı net'lerin telleri yalnızca dik açıyla, düz geçerek kesişir; kesişme düğüm değildir.
4. Tel, başka bir net'in ucundan ya da köşesinden geçmez ("T" görüntüsü bağlantı gibi okunur).
5. Aynı net'in üç ya da daha çok parçasının buluştuğu yere dolu nokta konur. **Nokta varsa bağlı,
   yoksa bağlı değil.**

### Çizimde değişenler

| Konu | Karar |
|---|---|
| PLC kartı | Kanal sembolü yalnızca ucunu çizer; bir kartın yan yana duran kanalları tek gövde kutusu içinde gösterilir. |
| Çapraz referans | Aynı uçtan aynı ada (potansiyele) giden bütün hedefler tek notta toplanır: `24VDC / 2.1, 4.1, 5.0`. |
| Not yeri | Yukarı çıkan uçların notu telin sağına, aşağı inenlerinki soluna yazılır; alt alta duran iki cihazın karşılıklı notları aynı yere düşmez. |
| Tag | Sembolün sayfadaki kutusunun solunda durur (sembol döndürülse de). Aynı cihazın bitişik sembollerinde bir kez yazılır. |

### Otomatik yerleşimde değişenler

| Konu | Karar |
|---|---|
| Baş aşağı çevirme | İki uçlu bir sembolün ilk ucu alttaki satıra, ikinci ucu üsttekine gidiyorsa sembol 180 derece çevrilir. Yoksa tel sembolün çevresinden dolaşır. |
| Aralıklar | İlk satır üstten 30 mm, satır aralığı 48 mm: uçların üstünde ve altında çapraz referans notlarına yer kalır. |

## Kalan kusurlar

- **Tel numarası ve not yazıları engel değildir.** Düğüm noktalarının yakınında tel numaraları üst
  üste binebilir ve bir tel başka bir notun üzerinden geçebilir. Bunlar okunaklılık kusurudur;
  yanlış bağlantı görüntüsü vermez.
- **Yol bulunamayan tel sessizce eski yöntemle çizilir.** Referans makinede böyle bir tel yok, ama
  çok sıkışık bir sayfada olabilir ve kullanıcı bunu fark etmez. ERC'ye ya da çıktıya uyarı eklenmeli.
- **Tel yolu her çizimde yeniden hesaplanır** ve modelde saklanmaz. Kullanıcı bir telin yolunu elle
  düzeltemez (Faz 6'da gerekecek).
- **Sayfa taşması hâlâ denetlenmiyor**; yerleşim hâlâ devre tipini tanımıyor (ADR-0009).
- **Performans ölçülmedi.** Referans makinede fark edilmiyor; yüzlerce telli bir sayfada ve
  300 sayfalık projede süre bilinmiyor.
