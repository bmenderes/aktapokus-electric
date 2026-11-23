# aktapokus-electric

Elektrik projelendirme (E-CAD) uygulaması. İş listesi ve fazlar: [ROADMAP.md](ROADMAP.md).
Kararlar: [docs/adr/](docs/adr/). Yeni bir işe başlamadan önce ikisini de oku.

## Çalışma kuralları

- Kullanıcıyla Türkçe konuş.
- Yalnızca bu proje klasöründe çalış; `.env` ve anahtar/parola dosyalarını okuma ya da yazma.
- Her faz başında kapsamı, etkilenen modülleri ve açık soruları yaz, **onay bekle**. Fazlar arasında onaysız geçiş yok.
- Faz içinde her iş kalemi ayrı commit: kod + test + gerekiyorsa ADR. Testi olmayan iş bitmiş sayılmaz.
- Mevcut testleri kıran değişiklik kabul edilmez.
- Spekülatif soyutlama yapma; yalnızca içinde bulunulan fazın ihtiyacını yaz.
- Teknik kararlar `docs/adr/NNNN-baslik.md` olarak kaydedilir. ADR-0001'deki bir kararı değiştirmek yeni ADR ister.
- Her faz sonunda `examples/` altındaki referans projeyle uçtan uca demo yapılır.
- Hedef makinede ek kurulum gerektirme: dağıtım self-contained, FreeCAD opsiyonel.

## Mimari kurallar

- **Model tek doğruluk kaynağıdır.** Editör, raporlar, 3D ve AI modelin istemcisidir.
- **Model yalnızca Command'lar üzerinden değişir.** Doğrudan model alanı değiştiren kod yazma.
- **`Ake.Core` bağımlılıksızdır:** proje ya da NuGet referansı alamaz; arayüz, dosya sistemi ve FreeCAD bilmez. `tests/Ake.Architecture.Tests` bunu doğrular.
- Diğer `src/` projeleri Core'a bağımlıdır; Core hiçbirine bağımlı değildir.
- Beklenen başarısızlık `Result` döner, programcı hatası exception fırlatır (ADR-0002).
- GPL bağımlılık ve QuestPDF eklenmez; yeni NuGet paketinde lisansı kontrol et.
- Hiçbir yerde (kod, arayüz metni, doküman) "EPLAN" marka adı kullanılmaz.
- Python yalnızca `tools/freecad/` altında.

## Komutlar

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet run --project src/Ake.Cli -- --version
dotnet run --project src/Ake.Editor
```

`ake` komutları (proje dosyası üzerinde çalışır, örnek: `examples/ref-machine/`):

```bash
dotnet run --project src/Ake.Cli -- validate examples/ref-machine/ref-machine.json
dotnet run --project src/Ake.Cli -- validate examples/pnomatik/pnomatik.json
dotnet run --project src/Ake.Cli -- apply <proje.json> <patch.json> --dry-run
dotnet run --project src/Ake.Cli -- renumber <proje.json> --wires --dry-run
dotnet run --project src/Ake.Cli -- tag-scheme <proje.json> sayfa --dry-run
dotnet run --project src/Ake.Cli -- macro create <proje.json> 2 --out motor.makro.json
dotnet run --project src/Ake.Cli -- macro insert <proje.json> examples/macros/motor-cikisi.makro.json 4 --dry-run
dotnet run --project src/Ake.Cli -- report all <proje.json> --format xlsx --out raporlar.xlsx
dotnet run --project src/Ake.Cli -- lib import examples/library/baslangic-seti.json --library <kutuphane.db>
dotnet run --project src/Ake.Cli -- template set <proje.json> examples/templates/a3-yatay.sablon.json
dotnet run --project src/Ake.Cli -- symbol import <proje.json> examples/qelectrotech
dotnet run --project src/Ake.Cli -- lib symbol-import <klasör> --library <kutuphane.db>
dotnet run --project src/Ake.Cli -- symbol import <proje.json> <cizim.dxf|.dwg> --license "<kullanım koşulu>" --scale 0.25 [--blocks]
dotnet run --project src/Ake.Cli -- symbol inspect <cizim.dxf|.dwg>
dotnet run --project src/Ake.Cli -- symbol import <proje.json> <cizim.dxf|.dwg> --license "<kullanım koşulu>" --alan "x,y,genişlik,yükseklik"
dotnet run --project src/Ake.Cli -- lib symbols <metin> --library <kutuphane.db>
dotnet run --project src/Ake.Cli -- import pdf <proje.json> <cizim.pdf> [--ramkayi-koru] [--dry-run]
dotnet run --project src/Ake.Cli -- render <proje.json> --pdf cikti.pdf --png onizleme [--icindekiler]
dotnet run --project src/Ake.Cli -- render <proje.json> --dxf cad --dwg cad
dotnet run --project src/Ake.Cli -- haberlesme examples/haberlesme/bus-topolojisi.json --cikti haberlesme.pdf
dotnet run --project src/Ake.Cli -- tekhat examples/ref-machine/ref-machine.json --cikti tekhat.pdf
```

Performans ölçümü (300 sayfalık sentetik proje; hedefler `docs/performans.md`'de):

```bash
dotnet run -c Release --project tools/Ake.Bench -- 300
```

Testler kullanıcının gerçek dosyalarına dokunmaz: kütüphane, proje ve rapor dosyaları her zaman
geçici klasörde açılır. Varsayılan kullanıcı kütüphanesi (`%LOCALAPPDATA%\aktapokus-electric\library.db`)
yalnızca `Program.cs` tarafından verilir; `CliApp.Run` onu parametre olarak alır.

Rapor snapshot'ları `tests/Ake.Reports.Tests/*.verified.txt` içindedir. Bir rapor bilinçli olarak
değiştiyse yeni `*.received.txt` dosyasını gözden geçir ve `verified` olarak yeniden adlandır;
okumadan onaylama. Testleri `DiffEngine_Disabled=true` ile çalıştır, yoksa Verify fark aracı açar.

Yeni bir command yazarken `tests/Ake.Core.Tests/Commands/CommandAssert.cs` içindeki `RoundTrips`
ve `Rejected` yardımcılarını kullan: çalıştır, geri al, yinele ve her adımda tutarlılık denetlenir.

`Ornek/` klasörü gerçek müşteri projesi içerir ve `.gitignore`'dadır; commit etme.

Hedef framework `net10.0` ve tek yerde, `Directory.Build.props` içinde tanımlı. .NET 10 SDK kurulu
olmayan makinede aynı komutlar `AkeTargetFramework` ortam değişkeniyle çalışır:

```powershell
$env:AkeTargetFramework = 'net9.0'
```

Paket sürümleri `Directory.Packages.props` içinde tutulur; `.csproj` dosyalarına sürüm yazma.

## Yapı

| Proje | Sorumluluk |
|---|---|
| `Ake.Core` | Domain model, Command'lar, ERC |
| `Ake.Serialization` | JSON proje formatı, şema versiyonlama |
| `Ake.Library` | Komponent kütüphanesi (SQLite), importer'lar |
| `Ake.Reports` | BOM, kablo, terminal, I/O listeleri |
| `Ake.Layout` | Şema sayfa yerleşimi |
| `Ake.Render` | Skia scene graph: ekran, PDF, SVG, DXF |
| `Ake.Plc` | Bus topolojisi, I/O adresleme, PLC export |
| `Ake.Panel` | 2D pano yerleşimi, FreeCAD köprüsü |
| `Ake.Ai` | LLM'den Command patch'i, doğrulama |
| `Ake.Cli` | `ake` komut satırı aracı |
| `Ake.Editor` | Avalonia uygulaması |

Çizim çıktısını değiştirdiysen `ake render --png` ile bir sayfayı görüntüye çevir ve gözle bak;
SVG snapshot'ı yalnızca değişikliği yakalar, doğru göründüğünü söylemez.

Editör penceresini görsel olarak denetlemek için **masaüstünde fare/klavye kullanma**; başsız
görüntü aracını kullan:

```bash
dotnet run --project tools/Ake.EditorSnapshot -- examples/ref-machine/ref-machine.json <çıktı-klasörü> [kutuphane.db] [arama]
```
