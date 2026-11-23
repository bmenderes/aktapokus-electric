# ADR-0018: Editörde sembol seçici

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0014 (E-CAD düzeni), ADR-0017 (paylaşılan sembol kütüphanesi)

## Bağlam

Paylaşılan sembol kütüphanesi (ADR-0017) yalnızca komut satırından kullanılabiliyordu. Kullanıcı editörde,
çizim yaparken kütüphaneden sembol arayıp seçebilmeli.

## Kararlar

| Konu | Karar |
|---|---|
| Yer | Navigator'ların yanında dördüncü sekme: **Semboller**. Arama kutusu, "Seçili cihaza uyanlar" seçeneği, sonuç listesi, önizleme ve "Seçili sembole ata" düğmesi. |
| Kütüphane | Editör açılırken kullanıcının varsayılan kütüphanesi (`%LOCALAPPDATA%\aktapokus-electric\library.db`) **varsa** açılır; yoksa oluşturulmaz. Sembol > Sembol kütüphanesi aç ile başka bir dosya seçilir. |
| Uyum süzgeci | "Seçili cihaza uyanlar" işaretliyken liste, çizimde seçili sembolün function'ının bağlantı noktası sayısı kadar ucu olan sembollerle sınırlanır. Seçim değişince liste yenilenir. |
| Önizleme | Sembol kendi kutusuna, kenar payıyla sığdırılır; uçlar mavi uzantı ve numarayla gösterilir. Büyük semboller kesilmez. |
| Atama | Çift tıklama ya da düğme: sembol projeye **kopyalanır** ve seçili function'a atanır; ikisi tek command'dır, Ctrl+Z ile birlikte geri alınır. Uç sayısı uymazsa hiçbir şey değişmez ve neden durum satırında görünür. |
| Arama | Ad ve kategoride; **Türkçe harflere ve aksanlara duyarsız**: "salter", "şalter" ve "ŞALTER" aynı sonucu verir; "ISIK" "ışık"ı bulur. SQLite'ın LIKE'ı yalnızca İngilizce harflerde büyük-küçük harf ayırmadığı için kütüphane açılırken bir `fold` işlevi kaydedilir; ürün araması da aynı işlevi kullanır (ADR-0007'deki "İ/ı eşleşmez" eksiği giderildi). Sonuç en çok 200 satırdır. |
| Oturum | Kütüphane, arama, önizleme ve atama `EditorSession` içindedir ve pencere açmadan test edilir. Oturum kütüphane dosyasını kapattığı için `IDisposable` oldu; pencere kapanınca kapatılır. |

## Doğrulama

Başsız görüntü aracıyla (`tools/Ake.EditorSnapshot`, yeni isteğe bağlı kütüphane ve arama
argümanlarıyla) QElectroTech koleksiyonunun 4.459 sembollük kütüphanesi açılıp "termik" arandı: liste,
uyum süzgeci ve önizleme doğru göründü. Fareyle atama gerçek pencerede denenmedi; oturum testleriyle
doğrulandı.

## Yapılmayanlar

- Sembolü listeden çizime sürükleyip bırakarak **yeni cihaz** yerleştirme.
- Kategori ağacıyla gezinme (şimdilik yalnızca arama).
- Ürüne varsayılan sembol bağlama (ADR-0017'den kalan).
- Uzun etiketler panelde kesiliyor; kategori satırın sonunda olduğu için çoğu zaman görünmüyor.
