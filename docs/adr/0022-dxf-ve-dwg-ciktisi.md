# ADR-0022: DXF ve DWG çıktısı (ACadSharp)

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-16
- **İlişki:** ADR-0001'deki "DXF: netDxf (MIT). DWG desteklenmez." kararının yerini alır.

## Bağlam

Kullanıcı çıktının PDF'in yanında DXF ve **DWG** olarak da alınabilmesini istiyor; ileride DXF/DWG
içe aktarma da gerekecek. ADR-0001 DXF için netDxf'i seçmiş, DWG'yi dışarıda bırakmıştı. GPL yasağı
LibreDWG'yi, ücret ODA'yı dışlıyor. ROADMAP, Faz 5 başında ACadSharp'ın (MIT) denenmesini istemişti.

## Deneme

ACadSharp 3.8.35 ile Türkçe metin, katman, renk ve çizgi kalınlığı içeren bir belge AC1015, AC1018,
AC1024, AC1027 ve AC1032 sürümlerinde DXF ve DWG olarak yazılıp aynı kütüphaneyle geri okundu.
Geometri, katman, mm birimi ve döndürme bütün sürümlerde korundu; **Türkçe harfler AC1018 ve
öncesinde bozuldu** (kod sayfası), AC1024 ve sonrasında doğru kaldı.

## Kararlar

| Konu | Karar |
|---|---|
| Kütüphane | **ACadSharp** (MIT; bağımlılıkları System.Memory ve System.Text.Encoding.CodePages, MIT). DXF de bununla yazılır; netDxf eklenmez. Tek kütüphane hem DXF hem DWG yazar, ileride ikisini de okur. |
| Sürüm | **AutoCAD 2013 (AC1027)**: Türkçe harfler korunur, 2013 ve sonraki CAD programları açar. |
| Dosyalar | Her sayfa ayrı dosya: `sayfa-<no>.dxf/.dwg`, içindekiler istenirse `icindekiler-<n>`. `ake render --dxf <klasör> --dwg <klasör>`. |
| Koordinat | mm; sahnenin Y'si aşağı, CAD'inki yukarı doğru olduğundan `y' = sayfa yüksekliği − y`. |
| Katmanlar | `CIZIM` (sembol, çerçeve, antet), `TEL` (renkli çizgiler), `METIN`. |
| Renk | Siyah ve renksiz öğeler AutoCAD rengi 7'dir (koyu arka planda beyaz, açıkta siyah); tel renkleri gerçek renk (RGB). |
| Çizgi kalınlığı | En yakın standart AutoCAD kalınlığı (0,25 → 0,25 mm; 0,5 → 0,50 mm). |
| Yazı | TEXT; yükseklik = yazı boyutu × 0,7 (CAD'de yükseklik büyük harf boyudur); sola, ortaya, sağa hizalama ve dönüş korunur; stil `Standard`, yazı tipi `arial.ttf`. |
| Dolu nokta | AutoCAD'in DONUT'u gibi: genişliği yarıçap olan, iki yaydan kapalı çizgi. |
| Taşınmayanlar | Görseller (logo) ve PDF bağlantıları. |

## Doğrulama

Referans makinenin bütün sayfaları DXF ve DWG olarak yazıldı ve geri okundu; öğe sayıları sahneyle
aynı. 2. sayfanın DWG'si geri okunup PNG'ye çizildi; özgün sayfayla aynı göründü.
Testler: `tests/Ake.Render.Tests/CadExportTests.cs`, CLI testi.
**AutoCAD, BricsCAD ya da LibreCAD'de açılarak denenmedi** (bu ortamda yok); doğrulama aynı
kütüphanenin okuyucusuyla yapıldı. Kullanıcı denetimi bekliyor.

## Yapılmayanlar

- Bütün sayfaların tek DWG'de, ayrı düzen sekmeleri (layout) olarak yazılması.
- Sembollerin CAD bloğu (BLOCK/INSERT) olarak yazılması; şimdilik her çizgi ayrı öğedir.
- Logonun DWG'ye gömülmesi (dış görsel referansı gerekir).
- DXF/DWG içe aktarma (Faz 6 sonrası; okuyucu hazır).
