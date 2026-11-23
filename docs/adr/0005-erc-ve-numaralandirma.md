# ADR-0005: ERC kuralları, numaralandırma ve şema 2

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15
- **Kaynak:** `docs/faz-2-command-erc.md`, sorular 11–21; önerildiği gibi onaylandı

## Bağlam

`Project.Create` yalnızca modelin iç tutarlılığını denetler (ADR-0003). Mühendislik hataları
ERC'nin işidir. Kuralların ne zaman bulgu ürettiği, kullanıcının raporu ciddiye alıp almayacağını
belirler: gürültülü bir rapor okunmaz.

## Kararlar

### Motor

| Konu | Karar |
|---|---|
| Kural | `IErcRule` uygulayan saf bir sınıf. Modeli değiştirmez; aynı model aynı bulguları aynı sırada verir. |
| Genişleme | Motor kural listesini parametre alır. Dışarıdan DLL yükleme ve kural yapılandırma dosyası yoktur. |
| Bulgu | Kural kimliği, şiddet (`Error` / `Warning` / `Info`), mesaj ve ilgili nesnelerin kimlikleri. |
| Bağlantı hesabı | Bir kez yapılır, bütün kurallara verilir. |

### Kurallar

| Kimlik | Şiddet | Ne zaman bulgu üretir |
|---|---|---|
| `erc.acik_uc` | Uyarı | Function **yarım bağlı**: bir ucu bağlı, diğeri boş. Hiç bağlanmamış function (yedek kontak, kullanılmayan kanal) bulgu üretmez. Potansiyel işareti taşıyan nokta bağlı sayılır. |
| `erc.kisa_devre` | Hata | Farklı adlı potansiyel işaretleri aynı potansiyele düşmüş. |
| `erc.tag_tekrar` | Hata | Aynı **tam** tag birden çok device ya da kabloda. Farklı yapı düğümleri altındaki aynı ürün tag'i tekrar değildir. |
| `erc.tel_no_tekrar` | Hata | Aynı tel numarası **farklı potansiyellerde**. Aynı potansiyelde aynı numara serbesttir. |
| `erc.kesit_koruma` | Hata | Telin ya da kablo damarının kesiti, potansiyeline dokunan en küçük koruma elemanının anma akımını taşıyamıyor. |
| `erc.io_atanmamis` | Uyarı | Kanalın ucu bağlı ama sembolik adı yok, ya da adı var ama ucu bağlı değil. |
| `erc.io_cift` | Hata | Aynı elektriksel uç, sembolik ad ya da adres birden çok kanalda. `*` içeren adres (otomatik adres) tekrar sayılmaz. |
| `erc.yetim` | Uyarı | Function'ı olmayan device, bağlantı noktası olmayan function, hiçbir bağlantıya atanmamış kablo damarı. |

### Kesit / koruma kuralının sınırları

- Taşıma tablosu tek ve basittir: bakır, PVC yalıtımlı tel, kanal içinde döşeme (EN 60204-1
  Tablo 6, B1 yöntemi değerlerine dayanır). Ortam sıcaklığı ve gruplama düzeltmesi yoktur.
  **Tablo değerleri standardın güncel baskısıyla karşılaştırılmadı; kullanılmadan önce doğrulanmalı.**
- Kesit tablodaki değerler arasındaysa aşağı yuvarlanır; 0,75 mm²'den ince kesitte kural sessizdir.
- Akımın yönü bilinmediği için kural koruma elemanının iki yanındaki potansiyele de bakar ve en
  küçük anma akımını esas alır. Bu, yanlış alarm vermez ama her hatayı da yakalamaz: kontaktörden
  sonraki tel, koruma elemanıyla aynı potansiyelde olmadığı için denetlenmez.
- Anma akımı ya da kesit girilmemişse kural sessiz kalır.

### Numaralandırma

| Konu | Karar |
|---|---|
| Yöntem | Servis modeli değiştirmez; `Retag` ve `AssignWire` command'larından oluşan bir transaction üretir. Yeniden numaralandırma tek adımda geri alınır ve audit'e yazılır. |
| Device tag'i | Aynı yapı düğümleri ve aynı harf öneki içinde, model sırasına göre 1'den başlayarak boşluksuz. "Harf + sayı" biçiminde olmayan tag'e dokunulmaz. |
| Tel numarası | İki şema: potansiyel bazlı (her potansiyele bir numara) ve sıralı (her tele ayrı numara). Kablo damarları numaralanmaz. |
| Sayfa/kolon şeması | Telin çizimdeki yerini gerektirir; Faz 5'e ertelendi. |

### Şema 2

| Konu | Karar |
|---|---|
| Değişiklik | Function'a isteğe bağlı `ratedCurrentA` (anma akımı, A) eklendi. |
| Sürüm politikası | Dosya biçimindeki **her** değişiklik, yalnızca alan eklense bile, `schemaVersion`'ı artırır. Böylece eski uygulama yeni dosyayı "bilinmeyen alan" yerine `proje.sema_yeni` ile reddeder. |
| Göç | Eski dosya yüklenirken sürüm sürüm ileri taşınır ve güncel şemayla kaydedilir. Şema 1 → 2 adımı veri dönüştürmez. |

### CLI

| Konu | Karar |
|---|---|
| Komutlar | `ake validate`, `ake apply`, `ake renumber`. |
| Çıkış kodu | 0 başarılı; 1 kullanım hatası, beklenen başarısızlık ya da ERC'de hata şiddetinde bulgu; 2 beklenmeyen hata. |
| `ake apply` ve ERC | Patch uygulanır, bulgular yazdırılır; hata varsa dosya **yine de yazılır** ve 1 ile çıkılır. `--strict` verilirse hata durumunda dosya yazılmaz. AI yolu (Faz 9) her zaman katı davranır. |
| Ayrıştırma | `System.CommandLine` (MIT). |

## Sonuçlar

- Referans makine ERC'den bulgusuz geçer; bu, kuralların gürültü üretmediğinin ölçüsüdür ve testle
  korunur.
- "Bilinçli boş bırakılmış uç" işareti yoktur. Yarım bağlı ama doğru olan bir devre (ör. tek ucu
  kullanılan değiştirmeli kontak) uyarı üretir; ihtiyaç olursa işaret eklenir.
- Tag numaralandırması var olan numaraları korumaz, yeniden dizer. Sahada etiketlenmiş bir panoda
  kullanılmadan önce `--dry-run` ile bakılmalıdır.
