# ADR-0002: Hata yönetimi ve exception politikası

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15

## Bağlam

Model yalnızca Command'lar üzerinden değişiyor (ADR-0001) ve aynı Command'ları editör, CLI, import
ve AI çağırıyor. Bir Command'ın reddedilmesi (geçersiz tag, olmayan terminal, ERC'den geçmeyen
patch) olağan bir durumdur; özellikle AI ve import akışında sık yaşanır. Çağıranın bu durumu
exception yakalayarak değil, dönüş değerine bakarak ele alması gerekiyor.

## Karar

**Beklenen başarısızlık `Result` döner, programcı hatası exception fırlatır.**

| Durum | Davranış |
|---|---|
| Kullanıcı girdisi, dosya içeriği ya da model durumu yüzünden işlem yapılamıyor | `Result.Failure(new Error(kod, mesaj))` |
| Argüman sözleşmesi ihlali (`null`, aralık dışı) | `ArgumentException` türevleri |
| Olmaması gereken durum (invariant bozuldu) | `InvalidOperationException` |
| Başarısız `Result<T>` üzerinde `Value` okumak | `InvalidOperationException` |

- `Error.Code` makine-okunur ve kararlıdır, `alan.sorun` biçiminde yazılır (ör. `device.tag_tekrar`).
  Testler ve arayüz koda bakar; `Error.Message` yalnızca insana gösterilir.
- `Ake.Core` exception yakalamaz ve log yazmaz; Serilog'a bağımlı değildir.
- Dış etki yapan projeler (`Ake.Serialization`, `Ake.Library`, `Ake.Panel` vb.) dosya ve süreç
  exception'larını kendi sınırlarında yakalayıp `Result`'a çevirir.
- Yakalanmamış exception'lar yalnızca en dışta, `Ake.Cli` ve `Ake.Editor` giriş noktalarında
  yakalanır ve Serilog ile `Fatal` olarak yazılır.

### Log

- Dosya: `%LOCALAPPDATA%\aktapokus-electric\logs\`, günlük dosya, son 7 gün saklanır.
- `Ake.Cli` uyarı ve üstünü ayrıca stderr'e yazar; stdout yalnızca komut çıktısı içindir.

### `ake` çıkış kodları

| Kod | Anlam |
|---|---|
| 0 | Başarılı |
| 1 | Kullanım hatası ya da beklenen başarısızlık |
| 2 | Beklenmeyen hata (ayrıntı log dosyasında) |

## Sonuçlar

- Command imzaları `Result` döner; çağıran başarısızlığı görmezden gelemez, kontrol etmek zorundadır.
- Core'da `try/catch` görülmesi bir kod kokusudur ve incelemede sorgulanır.
- `Result` bilinçli olarak küçük tutuldu (`Map`/`Bind` gibi zincirleme yardımcıları yok); ihtiyaç
  doğduğu fazda eklenir.
