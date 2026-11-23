# ADR-0007: Komponent kütüphanesi (Faz 4, birinci kısım)

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15
- **Not:** Proje sahibinin isteğiyle plan onayı beklenmeden, önerilen kararlarla yazıldı.

## Bağlam

Faz 4 yol haritasında on bir iş kalemi içeriyor; bunların yarısı (sembol içe aktarma, parametrik
sembol üretici, üretici veri biçimleri) henüz var olmayan çizim katmanına ya da tek tek büyük
içe aktarıcılara dayanıyor. Bu ADR, fazın DoD'sini karşılayan ve diğerlerinin üzerine kurulacağı
çekirdeği kapsar. Kalanlar "Yapılmayanlar" bölümündedir.

## Kararlar

### Katmanlar

| Konu | Karar |
|---|---|
| Ürün tanımı Core'dadır | `ProductDefinition` (üretici, sipariş kodu, function şablonları, montaj) ve `IProductCatalog` arayüzü `Ake.Core.Catalog` içindedir. Core, kütüphanenin SQLite olduğunu bilmez. |
| Device üretme | `ProductDefinition.CreateDevice` şablondan yeni kimliklerle bir Device kurar ama modele **eklemez**; ekleme yine `AddDevice` command'ıyla olur. Kütüphaneden ekleme için ayrı bir command yoktur. |
| Kütüphane | `Ake.Library` içinde `ComponentLibrary`; `IProductCatalog`'u uygular. |
| Patch | `addDevice` komutu `product` (sipariş kodu) ve isteğe bağlı `manufacturer` alır. Verilirse function'lar, ürün bilgisi ve montaj kütüphaneden gelir; aynı komutta ayrıca yazılamaz (`patch.urun_cakisma`). |
| Kopyalama, bağ değil | Device, üretildiği andaki ürün tanımının kopyasını taşır. Kütüphane sonradan değişirse proje değişmez; proje dosyası kütüphane olmadan da açılır. |

### Depolama

| Konu | Karar |
|---|---|
| Biçim | SQLite dosyası (ADR-0001). Sürüm `PRAGMA user_version` ile tutulur; daha yeni dosya açılmaz (`kutuphane.sema_yeni`). |
| Şema | `products`, `functions`, `connection_points`, `properties`. Function ve bağlantı noktası sırası saklanır. |
| Anahtar | Üretici + sipariş kodu tekildir. Aynı anahtarla yeniden içe aktarma kaydı bütünüyle değiştirir. |
| Özellikler | Serbest anahtar/değer/birim. Değer sayıya çevrilebiliyorsa ayrıca sayı olarak da saklanır (ileride aralık araması için). |
| Kaynak ve lisans | Her kayıtta `source` ve `license` zorunludur; eksikse içe aktarma reddedilir (`kutuphane.kaynak_yok`). |
| Atomiklik | Bir içe aktarma tek veritabanı işlemidir: bir ürün geçersizse hiçbiri yazılmaz. |
| Dosya kilidi | Bağlantı havuzu kapalıdır; kütüphane kapatılınca dosya hemen serbest kalır. |
| Paketler | Microsoft.Data.Sqlite (MIT), SQLitePCLRaw (Apache-2.0), SQLite (kamu malı). |

### Kütüphanenin yeri

- Varsayılan: `%LOCALAPPDATA%\aktapokus-electric\library.db`. `--library` ile başka dosya verilir.
- `ake apply`, varsayılan kütüphaneyi yalnızca dosya **varsa** açar; yoksa oluşturmaz.
- Testler varsayılan yolu hiç kullanmaz: `CliApp.Run` varsayılan yolu parametre olarak alır ve
  testler vermez.

### İçe aktarma

| Biçim | Kapsam |
|---|---|
| JSON | Tam ürün kaydı: function şablonu, montaj, özellikler. `source` ve `license` dosya düzeyinde verilir, ürün düzeyinde ezilebilir. Katı okunur. |
| CSV | Toplu ürün listesi. `manufacturer` ve `orderCode` sütunları zorunlu; bilinmeyen her sütun ürün özelliği olur. Function şablonu verilemez. Kaynak ve lisans `--source` / `--license` ile gelir. |

### CLI

```text
ake lib import <dosya.json|.csv> [--library <dosya>] [--source <metin>] [--license <metin>]
ake lib search <metin> [--library <dosya>]
ake lib show <sipariş-kodu> [--manufacturer <üretici>] [--library <dosya>]
ake apply <proje> <patch> --library <dosya>
```

### Başlangıç seti

`examples/library/baslangic-seti.json`: referans makinede kullanılan altı ürün. **Değerler
hatırlanarak elle girildi ve üretici kataloglarıyla karşılaştırılmadı**; dosyanın `source` alanı
bunu söyler. Örnek veridir; uygulama bu dosyayı kendiliğinden içe aktarmaz.

## Yapılmayanlar (Faz 4'ün devamı)

| İş kalemi | Neden ertelendi | Ne zaman |
|---|---|---|
| Sembol tablosu, SVG/DXF sembol içe aktarma, `.elmt` içe aktarıcı, parametrik sembol üretici | Sembolü çizecek ve doğrulayacak katman yok. | Faz 5 ile birlikte |
| ESI, GSDML, EDS, IODD içe aktarıcıları | PLC köprüsünün girdisidir; her biri ayrı ve büyük bir ayrıştırıcıdır. | Faz 7 |
| BMEcat + ETIM içe aktarıcı ve ETIM sınıfı → function şablonu eşlemesi | Gerçek bir katalog dosyasıyla denenmeden yazılamaz. | Proje sahibinden örnek dosya gelince |
| EDZ içe aktarıcı (deneysel), AAS | Öncelik sırası düşük. | Sonra |
| STEP referansı içe aktarma | Alan var (`stepRef`); kullanan yok. | Faz 8 |
| Eklenti (plug-in) mimarisi | İkinci içe aktarıcı yazılmadan arayüzü tahmin etmek spekülatif olur. | İlk üretici biçimi eklenirken |

## Bilinen eksikler

- **Ürün özellikleri projeye taşınmıyor.** Device yalnızca üretici, sipariş kodu, tip ve montajı
  kopyalar; güç, redüktör, bus adresi gibi özellikler kütüphanede kalır. Örnek projedeki
  motor/redüktör ve ada listeleri bu yüzden hâlâ üretilemiyor. Çözüm, Device'a özellik alanı
  eklemek (şema 3) ya da raporu kütüphaneyle birlikte üretmektir; karar Faz 5'ten önce verilecek.
- **Arama basittir:** metin içinde geçme, yalnızca ASCII harflerde büyük/küçük harf duyarsız
  (`İ`/`ı` eşleşmez). Özelliğe ya da sayı aralığına göre arama yok.
- **Kütüphaneden ürün silme ve dışa aktarma komutu yok.**
- **Kırpılmış yayında SQLite denenmedi.**
