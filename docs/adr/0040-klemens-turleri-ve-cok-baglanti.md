# ADR-0040: Klemens türleri (PE, sonlandırıcı) ve çok bağlantılı klemens

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-18
- **İlişki:** ADR-0030 (klemens şeridi), ADR-0034 (çok katlı klemens), ROADMAP Faz 6b-3

## Bağlam

ADR-0034 "Yapılmayanlar": çok bağlantılı klemens (taraf başına ikiden çok bağlantı), PE klemensi ve sonlandırıcı/ayırıcı levha.

## Karar

| Konu | Karar |
|---|---|
| Tür | `Function.Role` (`TerminalRole`): **Normal**, **ProtectiveEarth** (PE klemensi), **EndCover** (sonlandırıcı). Yalnızca klemens function'ında normal dışı olabilir (`function.tur_gecersiz`). |
| Çok bağlantılı klemens | Taraf başına 1–4 bağlantı (`Function.Ways`). Noktalar önce iç taraf, sonra dış taraf olarak sıralıdır (nokta sayısı = 2 × Ways). Adlar: ilk bağlantı eskisi gibi (`ad.a`, `ad.b`; katta `ad.a2`), ötekiler sonuna `.2`, `.3`… alır (`ad.a.2`, `ad.b2.3`). Hepsi aynı potansiyeldir (`PassThrough`). |
| Sonlandırıcı | Bağlantı noktası **taşımaz** (`Ways` 0, `PassThrough` yanlış); şeritte yerini ve klemens planında satırını ("SL (sonlandırıcı)") alır. ERC'nin "bağlantı noktası olmayan function" uyarısı sonlandırıcıyı saymaz. |
| Komutlar | `AddTerminal.Create(ad, kat, bağlantı, tür)`; `SetTerminalRole` yalnızca sıradan ↔ PE (sonlandırıcıya ya da sonlandırıcıdan geçiş yok: noktalar eklenip silinirdi). Bağlantı sayısı **sonradan değişmez** (`terminal.baglanti_degismez`: kaldırıp yenisini ekleyin). `SetTerminalLevel` bütün noktaları yeniden adlandırır. |
| Doğrulama | Taraf başına bağlantı 1–4 (`terminal.baglanti_gecersiz`); sonlandırıcıda nokta olamaz; `AddDevice` ve dosya yükleme aynı kuralları uygular. |
| Dosya | `role` yalnızca normal dışıysa yazılır; çok bağlantılı klemens zaten nokta listesidir. Şema numarası artmaz. Makro kopyası kat ve türü taşır (önceden kat taşınmıyordu; düzeltildi). |
| Rapor | Klemens planında bir tarafın **bütün** bağlantıları (hedef ve tel/damar) toplanır; ad `ad (PE)`, `ad (sonlandırıcı)`, `ad (2. kat)` biçiminde. |
| Çizim | PE klemensi: klemens simgesi ve yanında "PE". Çok bağlantılı: üstte iç, altta dış taraf noktaları (5 mm adım), ortada bara. Sonlandırıcı: ince, çapraz çizgili levha, ucu yok. |
| Güç zinciri | Çok bağlantılı klemenste zincir, öbür tarafın **bütün** noktalarından sürer (ADR-0039). |
| Editör | Klemens şeridi penceresinde satır başına **Bağl.** (1–4, yalnızca yeni satırda) ve **Tür** (Sıradan, PE, Sonlandırıcı; var olan klemenste yalnızca Sıradan ↔ PE). Köprü bayrakları ilk noktaya bağlanır, sonlandırıcıda pasiftir. |

## Yapılmayanlar

- Kat başına farklı bağlantı sayısı (katlar ayrı function olduğundan her kat kendi sayısını taşır, ama pencere katları toplu yönetmez).
- Köprü tarağı (kaç klemens, hangi ürün) ve sonlandırıcının ürün/sipariş kodu ayrı bir iş.
- PE klemensinin PE potansiyeline otomatik bağlanması ve PE dışı bir potansiyele bağlanırsa ERC uyarısı.
