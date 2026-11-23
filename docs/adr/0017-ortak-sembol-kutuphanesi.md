# ADR-0017: Paylaşılan sembol kütüphanesi

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0007 (komponent kütüphanesi), ADR-0013 (sembol tanımı), ADR-0016 (QElectroTech)

## Bağlam

QElectroTech koleksiyonunun tamamı bir projeye alınınca proje dosyası 60 MB oldu (ADR-0016). Semboller
proje başına değil, bir kez içe aktarılıp bütün projelerde kullanılmalıdır.

## Kararlar

| Konu | Karar |
|---|---|
| Yer | Semboller komponent kütüphanesi dosyasında (SQLite, ADR-0007) ayrı bir tabloda tutulur. Kütüphane şeması 2'ye çıktı; şema 1 dosyası açılınca sembol tablosu eklenir, ürünler korunur. |
| Biçim | Her sembol, proje dosyasındaki biçimle aynı JSON olarak saklanır (`SymbolJson`); ad, kategori, uç sayısı, kaynak ve lisans aramak için ayrı sütunlardır. Bu yüzden `Ake.Library`, `Ake.Serialization`'a bağlıdır (Core'a bağımlılık kuralı değişmez). |
| Kategori | İçe aktarılan klasörün göreli yolu (`10_electric/11_singlepole/200_fuses_protective_gears/10_fuses`). Eğik çizgi her zaman `/`. |
| Kimlik | İçe aktarıcının verdiği kimlik korunur (QElectroTech'te dosya yolundan türetilir). Yeniden içe aktarma günceller, çoğaltmaz. |
| Lisans | Lisansı olmayan sembol kütüphaneye alınmaz; içe aktarma tek işlemdir, biri reddedilirse hiçbiri yazılmaz. |
| Projeye kopyalama | Proje yalnızca kullandığı sembolleri taşır. Bir patch'teki `assignSymbol` projede olmayan bir sembol isterse sembol kütüphaneden bulunur, projeye **kopyalanır** ve atanır; ikisi tek adımdır ve birlikte geri alınır. Proje dosyası kütüphane olmadan da açılır ve çizilir. |
| Atıf | Sembol kimlikle, tek ise adla ya da `kategori/ad` yoluyla bulunur. Ad birden çok sembolde varsa (koleksiyonda "Sigorta" gibi adlar çok) atıf reddedilir ve yol ya da kimlik istenir. |
| Arama | Ad ve kategoride metin; isteğe bağlı uç sayısı süzgeci (ör. iki uçlu bir function'a uyan semboller). |
| Core arayüzü | `ISymbolCatalog` (Core.Catalog). Patch bu arayüzü kullanır; kütüphanenin SQLite olduğunu bilmez. |

### CLI

```text
ake lib symbol-import <dosya.elmt | klasör> [--library <dosya>] [--license <lisans>] [--scale <mm/piksel>]
ake lib symbols <metin> [--pins <n>] [--library <dosya>]
ake apply <proje> <patch> --library <dosya>      # assignSymbol kütüphaneden de bulur
```

`ake symbol import` (projeye doğrudan alma) kalır; küçük, projeye özel semboller içindir.

## Doğrulama

QElectroTech elektrik koleksiyonunun tamamı (7.190 eleman) kütüphaneye 8 saniyede alındı: 4.459 sembol,
52 MB. Bir projeye tek sembol atandığında proje dosyası 1,5 KB büyüdü (önceki yöntemde 60 MB).

## Yapılmayanlar

- **Editörde sembol seçici.** Editör henüz kütüphaneyi açmıyor; sembol, blok editörü ya da patch ile
  atanıyor. Navigator yanında aranabilir bir sembol listesi (sürükle-bırak ile atama) Faz 6'ya eklenecek.
- **Ürüne varsayılan sembol.** Kütüphane ürünü bir sembole bağlanamıyor; kütüphaneden eklenen cihaz hâlâ
  yerleşik sembolle çizilir.
- **Kütüphanedeki sembolün güncellenmesi projeye yansımaz.** Proje kopyayı taşır; bilinçli bir seçimdir
  (proje kütüphane olmadan açılmalı), ama "kütüphanedeki yeni sürümle güncelle" komutu yok.
- Kütüphaneden sembol silme ve dışa aktarma komutu yok.
