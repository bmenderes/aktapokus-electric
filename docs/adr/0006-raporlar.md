# ADR-0006: Raporlar

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15
- **Not:** Proje sahibi Faz 3'e plan onayı beklemeden, önerilen kararlarla devam edilmesini istedi.
  Bu ADR o kararların kaydıdır; ayrı bir plan belgesi yazılmadı.

## Bağlam

Raporlar modelin en çok kullanılan çıktısıdır: malzeme siparişi, kablaj ve devreye alma bu
listelerle yapılır. Mevcut projelerde listeler genellikle PDF'ten elle Excel'e aktarılıyor;
hedef bunların modelden doğrudan üretilmesidir.

## Kararlar

### Yapı

| Konu | Karar |
|---|---|
| Ara biçim | Her rapor önce biçimden bağımsız bir `ReportTable` üretir (sütunlar + satırlar). CSV ve XLSX yazıcıları aynı tabloyu yazar; yeni biçim eklemek rapor mantığına dokunmaz. |
| Saflık | Rapor üretimi modeli değiştirmez; aynı model her zaman aynı tabloyu verir. |
| Sütun anahtarı | Her sütunun kararlı bir anahtarı (`symbol`, `terminal`) ve gösterilen bir başlığı vardır. Şablonlar anahtara atıf yapar; başlık değişse de şablon bozulmaz. |
| Şablon | JSON dosyası: hangi sütunlar, hangi sırayla, hangi başlıkla. Bilinmeyen anahtar hatadır ve geçerli anahtarlar mesajda listelenir. |
| Hücre tipi | `string`, `int`, `decimal` ya da boş. |

### Raporlar

| Rapor | İçerik |
|---|---|
| `bom` | Ürün bilgisi olan device'lar üretici + sipariş koduna göre, kablolar tipine göre gruplanır. Ürün bilgisi **olmayan** device'lar gruplanmaz, tek tek listelenir: eksik veri görünür kalsın. |
| `cables` | Damar başına bir satır, iki ucuyla. Kullanılmayan damar boş uçlarla listelenir. |
| `wires` | Her bağlantı bir satır (tel, kablo damarı, köprü). Numaralı teller sayı sırasıyla önce gelir. |
| `terminals` | Klemens başına bir satır: iç hedef, dış hedef, tel/damar, köprü, potansiyel. |
| `io` | Kanal başına bir satır. "Klemens" ve "Kod" sütunları kanalın potansiyelinden çıkarılır: geçirgen function'lar klemens, diğer uçlar saha elemanıdır. |

### Klemensin iç ve dış tarafı

Klemens function'ının **ilk** bağlantı noktası iç (pano içi), **ikinci** bağlantı noktası dış (saha)
taraftır. ADR-0003 bu bilgiyi nokta adına bırakmıştı; klemens planı için sıra kuralı yeterli oldu
ve şema değişmedi. Referans makinenin giriş klemensleri bu kurala göre düzeltildi.

### Biçimler

| Konu | Karar |
|---|---|
| CSV | Makine biçimi: ayraç virgül (`--separator` ile değişir), ondalık ayraç nokta, satır sonu LF. Dosyaya yazılırken UTF-8 BOM eklenir (Excel Türkçe karakterleri BOM olmadan bozar). |
| XLSX | İnsan biçimi: rapor başına bir sayfa, kalın ve dondurulmuş başlık, filtre. Sayılar sayı olarak, metinler **metin** olarak yazılır; `1.2` gibi klemens adları sayıya ya da tarihe çevrilmez. |
| Sütun genişliği | İçerik uzunluğundan tahmin edilir. ClosedXML'in `AdjustToContents` işlevi kullanılmaz: yazı tipi ölçümü ister ve yazı tipi kurulu olmayan makinede farklı sonuç verir. |
| Kütüphane | ClosedXML 0.105.1 (MIT). Geçişli bağımlılıkların tamamı MIT ya da Apache-2.0. |

### CLI

```text
ake report <bom|cables|wires|terminals|io> <proje.json> [--format csv|xlsx] [--out <dosya>]
                                                        [--template <şablon.json>] [--separator <c>]
ake report all <proje.json> --format xlsx --out <dosya>
```

`all` bütün raporları tek çalışma kitabına yazar (rapor başına bir sayfa).

### Test

Referans makinenin beş raporu Verify ile snapshot'lanır (`tests/Ake.Reports.Tests/*.verified.txt`).
Snapshot'lar CSV metnidir; XLSX ikili ve zaman damgalı olduğu için dosya geri okunarak doğrulanır.

## Bilinen eksikler

- **Motor/redüktör ve ada listeleri yok.** Örnek projedeki bu iki liste motor tipi, redüktör,
  sürücü, bus adresi, güç, sensör tipi gibi teknik özellikler ister; modelde bu alanlar henüz yok.
  Faz 4'te kütüphane ürün özellikleri geldiğinde eklenecek.
- **Yan yana blok yerleşimi yok.** Örnek Excel'de I/O listesi modül başına yan yana bloklar
  halindedir; burada tek düz tablo üretilir. Sütun seçimi şablonla yapılır, sayfa düzeni yapılamaz.
- **BOM açıklaması** gruptaki ilk device'ın açıklamasıdır ("Motor kontaktörü 1", adet 2). Doğru
  açıklama kütüphane ürün kaydından gelmelidir (Faz 4).
- **Tag'ler tam yazılır** (`=M1+P1-K1:13`). Sayfa bazlı kısa yazım (`3F3`) tag
  şeması eklendiğinde (Faz 5) raporlara da yansıyacak.
- **SixLabors.Fonts 1.0.0'da kalmalıdır.** 2.x sürümleri koşullu lisansa geçti; ClosedXML
  yükseltilirken geçişli bağımlılık kontrol edilmelidir.
- **Trimming denenmedi.** ClosedXML reflection kullanır; `Ake.Cli` kırpılarak yayımlandığında
  XLSX çıktısının çalıştığı doğrulanmadı.
