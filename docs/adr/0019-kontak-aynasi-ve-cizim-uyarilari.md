# ADR-0019: Kontak ayna tablosu, bobin referansı ve çizim uyarıları

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0009 (şema çizimi), ADR-0011 (tel yolu)

## Bağlam

Kontaktör ve rölelerin bobini ile kontakları çoğu zaman farklı sayfalardadır. Şemayı okuyan kişi bir
kontağın hangi bobine bağlı olduğunu ve bir bobinin kontaklarının nerede kullanıldığını sayfayı
çevirmeden görebilmeli. Ayrıca tel yönlendirici (ADR-0011) yol bulamadığında tel sessizce düz
çiziliyordu; hata gözden kaçıyordu.

## Kararlar

| Konu | Karar |
|---|---|
| Ayna tablosu | Bobini olan cihazda bobin sembolünün alt ucundan inen telin sağına, cihazın her kontağı için bir satır yazılır: tür (`G` güç, `NO`, `NC`), uç numaraları ve kontağın çizildiği `sayfa.kolon`. Hiçbir sayfada olmayan kontak `—` ile gösterilir. |
| Tablonun yeri | Uçların dik yazılan sayfa notlarının (ör. `-K0:14 / 5.2`) altına düşsün diye bobin ucundan 15 mm aşağıda başlar. Kapladığı yer tel yolu için engeldir. |
| Bobin referansı | Kontak sembolünde tag'in altına bobinin yeri `(sayfa.kolon)` yazılır; yan yana duran kutuplardan yalnızca tag'i yazılan ilkine. |
| Yolsuz tel | Yol bulunamazsa tel uçtan uca düz ve **macenta** (`#e0007a`) çizilir; renk kodlu tellerin hiçbir rengiyle karışmaz. Sahneye bir uyarı eklenir: tel numarası ve iki ucu. |
| Uyarıların gösterimi | `Scene.Warnings` listesi. `ake render` her uyarıyı `UYARI sayfa N: …` olarak hata akışına yazar (çıkış kodu değişmez); editör seçili sayfanın uyarılarını durum satırında gösterir. |

## Doğrulama

Referans makine `ake render --png` ile çizildi: 2. sayfada K1 bobininin altında beş satırlık tablo
(`G 1-2 2.0` … `NC 21-22 2.2`), güç kontağının tag'i altında `(2.1)` göründü; notlarla çakışma yok.
Testler: `tests/Ake.Render.Tests/CrossReferenceRenderTests.cs`.

## Yapılmayanlar

- Tablo yeri sabit pay ile seçilir; çok uzun sayfa notlarında yine çakışabilir. Gerçek ölçüm sonraki iş.
- Yardımcı kontak blokları ve termik röle kontakları için ayrı ayna biçimi yok.
- Ayna tablosu cihaz kutusu (kontaktör seçimi) değiştirilerek biçimlendirilemiyor.
