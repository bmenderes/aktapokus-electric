# ADR-0042: Sayfa alt numaraları (3.1) ve baştan numaralama

Durum: kabul edildi

## Bağlam
Proje bittikten sonra araya sayfa eklenince sonraki sayfaların numaraları kayarsa çapraz referanslar, içindekiler ve tel numaraları bozulur.

## Karar
- `AddPage` isteğe bağlı sıra alır; araya eklenen sayfa sonrakileri yeniden numaralamaz.
- Yeni sayfa alt numara alır: 3 → 3.1, 3.1 → 3.2 (`PageNumbering.NextSubNumber`, ana numaradaki en büyük alt numaranın bir fazlası).
- `SetPageNumber` numarayı değiştirir (boş/boşluklu/16+ karakter ve tekrar reddedilir).
- Baştan numaralama yalnızca kullanıcı isterse: `PageNumbering.Renumber` sayfaları 1..N yapar, tek adımda geri alınır.
- Çapraz referans ayracı: alt sayfada `/` (3.1/4), normal sayfada `.` (3.4); böylece alt sayfa ile kolon karışmaz.
- Çapraz referanslar ve içindekiler numarayı sayfadan okuduğu için kendiliğinden güncellenir. Dosya şeması değişmez.
- Editör: Sayfa sekmesi › Numaralandırma (`sayfa.araya_ekle`, `sayfa.numaralari`).

## Sonuç
Numaralar yalnızca kullanıcı isteyince değişir; tel numaralandırmasıyla (ADR-0041) aynı "kaldığı yerden devam" ilkesi.
