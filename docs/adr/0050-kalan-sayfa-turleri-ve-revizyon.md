# ADR-0050: Potansiyel özeti, pin-bağlantı, PLC, revizyon özeti ve çerçeve dokümantasyonu sayfaları; revizyon geçmişi

Durum: kabul edildi (2026-11-19, proje sahibi: "kalan sayfa türleri"; Faz 6d)

## Karar
ADR-0046'daki otomatik tablo sayfası mekanizmasına beş tür eklenir (antetli gerçek sayfa, numaralı, PDF'e girer, devam sayfalarıyla sürer):
- **PotentialOverview** — iki ya da daha çok uçlu her potansiyel: adı (kaynak adı; çakışma varsa "(çakışma)"), tel numaraları (sayısal sırayla), uç sayısı, uçlar (ilk 8, fazlası "…").
- **PinConnections** — her bağlantının iki ucu için bir satır: cihaz, uç, hedef, tel numarası ya da kablo damarı; cihaz sırasıyla.
- **PlcDiagram** — modül kanalları, adresleri, adları, bağlı saha uçları. (İlk sürümde tablo sayfasıydı; ADR-0053 ile modül kutuları çizen gerçek PLC şemasına dönüştü.)
- **RevisionOverview** — revizyon geçmişi: işaret, tarih, hazırlayan, açıklama.
- **FrameDocumentation** — projedeki her sayfa şablonu: kâğıt, kolon×satır, kenar boşluğu, ızgara bandı, antet yüksekliği, şablonu kullanan sayfa sayısı.

**Revizyon geçmişi (model):** `Revision(Mark, Date, Author, Description)` kaydı `Project.Revisions` listesindedir; komut `AddRevision` (işaret boş/boşluklu/8 karakterden uzun olamaz, açıklama boş olamaz, işaret tekil olmalı; geri alınır). `RevisionNumbering.NextMark`: ilk A, sonra B…Z, AA…; sayısal işaret 1, 2, 3…
Dosyada isteğe bağlı `revisions` listesi (boşsa yazılmaz; şema numarası artmaz).
Editör "Revizyon ekle…" (Başlangıç sekmesi › Proje) sıradaki işareti, bugünün tarihini ve hazırlayanı (`drawnBy`) önerir; Tamam hem kaydı ekler hem antetteki `revision` özelliğini o işarete getirir (tek adım, tek Geri Al).

## Bu dilimde olmayanlar
Modül kutuları çizen PLC şeması, revizyonun hangi sayfaları etkilediğini işaretleme (revizyon bulutu), revizyon kaydı silme/düzenleme.
