# ADR-0004: Command API, patch formatı ve audit log

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15
- **Kaynak:** `docs/faz-2-command-erc.md`, sorular 1–10; önerildiği gibi onaylandı

## Bağlam

Model yalnızca Command'lar üzerinden değişir (ADR-0001). Editör, CLI, import ve AI aynı yolu
kullanacağı için command'ların davranışı, dosyadaki karşılığı (patch) ve bıraktığı iz (audit)
bir kez ve ortak tanımlanmalıdır.

## Kararlar

### Command

| Konu | Karar |
|---|---|
| Sözleşme | `Execute` ön koşul sağlanmıyorsa modeli **hiç değiştirmeden** `Result.Failure` döner. `Undo` yalnızca başarıyla çalışmış command'da çağrılır ve başarısız olamaz. |
| Undo yöntemi | Ters işlem. Her command geri almak için gerekeni kendi tutar; modelin anlık görüntüsü alınmaz. |
| Undo'nun kesinliği | Undo modeli bayt bayt eski haline getirir: silinen nesneler listedeki özgün konumlarına döner. Testler bunu serileştirilmiş metni karşılaştırarak doğrular. |
| Redo | Aynı command nesnesi yeniden çalıştırılır; üretilen kimlikler değişmez. |
| Tutarlılık | Her başarılı command'dan sonra `Project.CheckIntegrity()` başarılıdır. |
| Transaction | Birden çok command tek adımdır. Biri başarısız olursa öncekiler ters sırayla geri alınır. Undo yığınında tek kayıttır. |
| Device silme | Zincirlemedir: bağlantılar, device üzerindeki potansiyel işaretleri ve yerleşimler kaldırılır, function'larına atanmış PLC kanalları boşaltılır. Undo hepsini geri getirir. |
| PLC'ye bağlı device | Controller, node ya da modül olan device silinemez (`device.plc_bagli`). PLC ağacını düzenleyen command'lar Faz 7'dedir. |
| Command kümesi | `AddDevice`, `RemoveDevice`, `Connect`, `Disconnect`, `AssignWire`, `Retag`, `AssignIo`. Yapı düğümü, kablo, sayfa ve yerleşim command'ları ihtiyaç duyan fazda eklenir. |
| Yapı düğümleri | `AddDevice` ve `Retag` var olmayan yapı düğümünü yaratmaz, hata verir. |

### Patch

| Konu | Karar |
|---|---|
| Anlam | Patch bir transaction'ın dosyadaki halidir: ya tamamı uygulanır ya hiçbiri. |
| Sürüm | `patchVersion`, proje `schemaVersion`'ından ayrıdır. |
| Atıf | Nesnelere kimlikle (GUID) ya da tag'le (`-K1:A1`, `=M1+P1-K1`) atıfta bulunulur. Tag'de yazılmayan aspect eşleşmeyi daraltmaz; birden çok nesne uyuyorsa patch reddedilir (`patch.tag_belirsiz`). |
| Çözme zamanı | Atıflar her adım çalışırken, modelin o andaki durumuna göre çözülür. Böylece bir adım, önceki adımın eklediği nesneye tag'iyle atıfta bulunabilir. |
| Yeni kimlikler | İsteğe bağlıdır; verilmezse uygulama üretir. |
| Kısmi güncelleme | `retag` ve `assignIo` içinde yazılmayan alan değişmez. Sonuç: patch ile bir alan boşaltılamaz; bu, ihtiyaç doğduğunda açık bir "temizle" komutuyla çözülür. |
| Katı okuma | Bilinmeyen komut, bilinmeyen alan ve eksik zorunlu alan hatadır. |

### Audit

| Konu | Karar |
|---|---|
| Kayıt | Her `Execute`, `Undo` ve `Redo` ayrı kayıttır: zaman (UTC), kaynak (`User` / `Ai` / `Import`), kişi, eylem, özet. Kayıt silinmez. |
| Dosya | Proje dosyasının yanında, `proje.audit.jsonl`: satır başına bir JSON kaydı, yalnızca sona eklenir. Proje dosyasının içinde tutulmaz. |
| Kişi | Patch'teki `actor`, yoksa `--actor`, yoksa işletim sistemi kullanıcı adı. Kimlik doğrulama yoktur; bu izlenebilirlik kaydıdır, güvenlik kaydı değil. |

## Sonuçlar

- Yeni bir düzenleme özelliği önce command olarak yazılır ve `CommandAssert.RoundTrips` ile
  (çalıştır, geri al, yinele, her adımda tutarlılık) test edilir.
- Ters işlem yöntemi bellek dostudur ama her command'ın undo'su ayrıca yazılır ve test edilir.
- Audit dosyası sınırsız büyür; döndürme ya da arşivleme şimdilik yoktur.
- Proje dosyası ile audit dosyası iki ayrı yazmadır; ikincisi başarısız olursa proje yazılmış,
  kayıt eksik kalır. Komut bu durumda hata koduyla çıkar.
