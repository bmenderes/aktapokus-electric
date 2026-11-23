# PDF'ten düzenlenebilir içe aktarma: inceleme ve plan

Durum: **Aşama 1 ve 2 uygulandı** (2026-11-17, ADR-0028); Aşama 3 (tanıma) Faz 6 sonrası, onay bekliyor

## Amaç

Başka bir E-CAD aracında (SEE Electrical) çizilmiş eski projeleri, elde yalnızca PDF çıktısı olsa bile,
bu uygulamada **düzenlenebilir** açmak. Amaç, eski projelerdeki birikimin (devreler, semboller, yapı)
kaybolmaması.

## Örnek PDF yapısının incelenmesi (genel bulgular)

Gerçek bir proje PDF'i yerelde incelendi (dosya depoya girmez); aşağıda yalnızca **genel yapı bilgisi** yer alır, kaynak projeye
ait hiçbir içerik (ad, tag, sayfa başlığı, sayı) bu belgeye yazılmadı:

| Bulgu | Değer | Sonuç |
|---|---|---|
| Üretici | Rapor motoruyla üretilmiş vektör PDF | Taranmış görüntü değil, vektör |
| Boyut | Onlarca A3 yatay sayfa, birkaç MB | Küçük; hızlı okunur |
| Çizim | Sayfa başına yüzlerce çizim nesnesi, **hepsi düz çizgi** (daire ve yaylar çizgi parçalarına bölünmüş); az sayıda eğri | Geometri tam ve ölçekli alınabilir; daire ve yay çizgi parçalarından yeniden kurulur |
| Metin | **Gerçek metin** (Türkçe harfler dahil), konumlarıyla | Tag, uç adı, çapraz referans ve antet metni okunabilir; OCR gerekmez |
| Renk ve kalınlık | Siyah ana çizim, yeşil, mavi, turuncu çizgiler; birkaç kalınlık | Tel / PE / sinyal ayrımına ipucu |
| **Yer imleri** | Çok düzeyli: kategori → sayfa (numara ve başlık) → o sayfadaki **cihaz tag listesi** | Sayfa yapısı ve her sayfadaki cihazlar çizim okunmadan bilinir |
| Antet | Proje, çizim no, tarih, rev., sayfa/toplam sayfa metin olarak | Kendi antetimizin alanlarına eşlenir |
| Gruplama | Yok: sembol, tel, etiket ayrı ayrı çizgi ve kelime | Sembol ve bağlantıyı tanımak çıkarım ister (aşağıda Aşama 3) |

Kaynak proje dosyası parolalı olduğu için okunmaz (kural: parola aşılmaz). Excel I/O listesi ayrıca içe aktarılabilir (Faz 7 ile birlikte).

## Yaklaşım: üç aşama

**Aşama 1 — Serbest grafik (modelde yeni nesne).** Sayfaya bağlı çizgi, çoklu çizgi, daire, yay ve yazı
nesneleri; elektriksel anlamı yoktur. Command'larla eklenir, taşınır, silinir, yazısı düzenlenir (ADR-0004).
Çizim, seçim, PDF/SVG/DXF/DWG çıktısı onları da kapsar. ROADMAP'te "bütün bir DXF/DWG çizimini sayfaya
almak" için de bu nesne bekleniyordu: **tek temel iki işi karşılar.** JSON şeması 6 olur; eski dosyalar
göç eder.

**Aşama 2 — PDF okuyucu.** Her PDF sayfası bir proje sayfası olur: çizgiler ve metinler serbest grafik
olarak, birebir konum ve kalınlıkla gelir. Yer imlerinden sayfa numarası, başlık ve gruplama kurulur. Kaynağın
çerçeve ve antedi atılır ya da korunur (seçenek); antet metni kendi şablonumuzun alanlarına yazılır.
Sonuç: eski proje **düzenlenebilir bir çizim olarak açılır**; yazılar düzeltilir, parçalar taşınır, sayfa
çıktıya alınır. Elektriksel model yoktur; ERC, raporlar ve numaralandırma bu sayfalarda çalışmaz.

**Aşama 3 — Tanıma (yarı otomatik, onaylı).** Grafik, modele çevrilir:
- Yer imlerindeki ve metindeki tag'ler → cihazlar; uç adları → bağlantı noktaları.
- Çizgi bağlantıları → teller ve potansiyeller; renk → tel rengi ipucu.
- Çizgi kümeleri → kullanıcı sembolü. Aynı şekil bir kez tanımlanır; böylece **eski projelerden kendi sembol
  kütüphanemiz** oluşur.
- Tanınan sayfa seçilip **makroya** çevrilir (mevcut makro altyapısı): eski devreler yeniden kullanılır.
Tanıma tahminidir; her öneri listede onaylanır ya da düzeltilir, ERC sonucu doğrulama olarak kullanılır.
Kalan grafikler yerinde durur.

## Öncelik önerisi

Aşama 1 + 2 önce: eski projeler **hemen açılabilir** ve içerik kaybolmaz. Aşama 3 editörün temel işleri
(Faz 6) bittikten sonra, onay arayüzüyle birlikte. Aşama 1 zaten Faz 6'nın eksiğidir.

## Alternatif ve tamamlayıcı yol

SEE'den **DXF/DWG** dışa aktarılabiliyorsa tanıma çok daha güvenilir olur: bloklar (= semboller), katmanlar ve
blok öznitelikleri (= tag, uç adları) çizgi yığını yerine yapı olarak gelir. DXF/DWG okuyucu hazır (ADR-0027).
PDF yolu, DXF'e erişilemeyen ya da müşteriden yalnızca PDF gelen projeler içindir.

## Etkilenen modüller

`Ake.Core` (serbest grafik modeli, command'lar), `Ake.Serialization` (şema 6, göç), `Ake.Render` (çizim ve çıktı),
`Ake.Library` ya da yeni `Ake.Import` (PDF okuyucu; DXF okuyucuyla ortak kod), `Ake.Editor` (seçim, taşıma,
yazı düzenleme), `Ake.Cli` (`ake import pdf`).

## Açık sorular

1. **PDF kütüphanesi:** Python (PyMuPDF) hedef makinede kurulamaz ve AGPL'dir; kullanılmaz. Aday: PdfPig
   (Apache-2.0, saf C#, çizim yolu ve harf konumu okur). Eklenmeden önce lisans ve yol okuma yeteneği doğrulanır.
2. SEE Electrical'dan DXF/DWG dışa aktarılabiliyor mu? (Varsa Aşama 3 önce oradan test edilir.)
3. Kaynağın çerçeve ve anteti atılsın mı, korunsun mu? Öneri: atılsın, antet alanları aktarılsın.
4. PDF'teki karakter kalınlıkları ve yazı tipleri: Arial'a eşlenir; farklı yazı tipi çıkarsa varsayılan kullanılır.
