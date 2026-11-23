# ADR-0001: Değişmez kararlar

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-15
- **Kaynak:** `ROADMAP.md`, Bölüm 0

## Bağlam

aktapokus-electric, elektrik projelendirme (E-CAD) uygulamasıdır. Proje dokuz faza yayılıyor ve
her fazda aynı temel sorular yeniden açılabilir: hangi dil, hangi arayüz kütüphanesi, hangi dosya
formatı, hangi lisans. Bu kararlar projenin başında bir kez verildi ve burada kayda geçirildi.
Bu ADR'deki bir kararı değiştirmek için onu geçersiz kılan yeni bir ADR yazılır.

## Kararlar

### Mimari

| Konu | Karar |
|---|---|
| Mimari | **Text-first core.** Elektriksel model tek doğruluk kaynağıdır; editör, raporlar, 3D ve AI bu modelin istemcisidir. |
| Değişiklik mekanizması | Model **yalnızca Command'lar üzerinden** değişir. Editör, AI, import ve konfigüratör aynı yolu kullanır. Undo/redo ve audit log buradan gelir. |
| Bağımlılık yönü | `Ake.Core` arayüze, dosya sistemine ve FreeCAD'e bağımlı olamaz; bu kural testle doğrulanır. |
| AI | Opsiyonel modüldür; kapalıyken hiç yüklenmez. |

### Teknoloji

| Konu | Karar |
|---|---|
| Dil / runtime | C# / .NET 10 LTS |
| Arayüz | Avalonia (MIT) + SkiaSharp. WPF kullanılmaz. |
| Proje dosyası | Canonical format JSON (diff'lenebilir, git uyumlu). |
| Komponent kütüphanesi | SQLite |
| Grafik | Kendi scene graph'ımız (Skia). SVG ve DXF yalnızca import/export içindir. |
| DXF | netDxf (MIT). **DWG desteklenmez.** |
| PDF | SkiaSharp `SKDocument`. QuestPDF kullanılmaz (lisansı koşullu). |
| Excel | ClosedXML (MIT) |
| 3D | FreeCAD headless (`freecadcmd`, LGPL) + OpenCascade STEP |
| Log | Serilog |
| Test | xUnit + snapshot testleri (Verify) |
| Python | **Yalnızca** `tools/freecad/` altında. Uygulama ve core C#'tır. |

### Lisans ve veri

| Konu | Karar |
|---|---|
| Lisans | Kendi kodumuz Apache-2.0. GPL bağımlılık **eklenmez**. |
| Üretici verisi | Uygulama üretici kütüphanesi **dağıtmaz**; kullanıcı import eder. Her kayıtta `source` ve `license` alanı zorunludur. |
| İsimlendirme | Hiçbir yerde "EPLAN" marka adı kullanılmaz. |

### Ürün

| Konu | Karar |
|---|---|
| Arayüz paradigması | EPLAN benzeri kullanım alışkanlığı: navigator'lar, dock'lanabilir paneller, özellik diyaloğu. Görsel kopya değil, iş akışı benzerliği. |
| Standart | IEC 81346 reference designation (`=` fonksiyon, `+` yer, `-` ürün) ve IEC 60617 semboller. |

### Dağıtım ve performans

| Konu | Karar |
|---|---|
| Dağıtım | Self-contained + trimming; mümkün olan projelerde Native AOT. Kurulum boyutu **80 MB altı**. |
| Minimum sistem | Windows 10/11 x64, **4 GB RAM**, 2 çekirdek, entegre GPU. Skia CPU fallback çalışmalıdır. |
| Soğuk açılış | 3 saniyenin altı |
| Render | 300 sayfalık projede açık sayfa dışında render yapılmaz. |
| Bellek | AI kapalıyken 300 MB altı |

## Sonuçlar

- Core'un bağımlılıksız kalması, aynı modelin CLI, editör ve AI tarafından kullanılmasını sağlar;
  karşılığında her dış etki (dosya, render, FreeCAD) ayrı bir projede yaşamak zorundadır.
- Tek değişiklik yolu Command olduğu için her yeni düzenleme özelliği önce bir Command olarak
  yazılır; doğrudan model alanı değiştiren kod kabul edilmez.
- Self-contained dağıtım, hedef makinede .NET kurulumu gerektirmez. Trimming ve Native AOT,
  reflection'a dayanan kütüphanelerin seçimini kısıtlar; JSON serileştirme source generator ile yapılır.
- GPL yasağı ve QuestPDF'in dışlanması, her yeni NuGet paketinde lisans kontrolünü zorunlu kılar.
- DWG desteklenmediği için DWG ile çalışan kullanıcılar dosyalarını DXF'e çevirerek getirir.
- FreeCAD opsiyonel harici bileşendir; kurulu değilse uygulama çalışır, yalnızca STEP montaj
  çıktısı pasif kalır.
