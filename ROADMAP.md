# aktapokus-electric — Geliştirme İş Listesi

> Claude Code için ana yönerge. Bu dosyayı repo köküne `ROADMAP.md` olarak koy. CLAUDE.md'den referans ver.

---

## 0. Değişmez Kararlar (ADR-0001 olarak kaydet)

| Konu | Karar |
|---|---|
| Mimari | **Text-first core.** Elektriksel model tek doğruluk kaynağı (single source of truth); editör, raporlar, 3D ve AI bu modelin istemcisi |
| Değişiklik mekanizması | Model **yalnızca Command'lar üzerinden** değişir (editör, AI, import, konfigüratör aynı yolu kullanır). Undo/redo + audit log buradan gelir |
| Dil / runtime | C# / .NET 10 LTS |
| UI | Avalonia (MIT) + SkiaSharp. WPF yok |
| Proje dosyası | Canonical format: **JSON** (diff'lenebilir, git uyumlu). Komponent kütüphanesi: SQLite |
| Grafik | Kendi scene graph'ı (Skia). SVG/DXF sadece import/export |
| DXF / DWG | ACadSharp (MIT), AutoCAD 2013 biçimi (ADR-0022; ADR-0001'deki netDxf ve "DWG yok" kararının yerine) |
| PDF | SkiaSharp `SKDocument` (ücretsiz). QuestPDF kullanma (lisans koşullu) |
| Excel | ClosedXML (MIT) |
| 3D | FreeCAD headless (`freecadcmd`, LGPL) + OpenCascade STEP |
| Log | Serilog |
| Test | xUnit + snapshot testleri (Verify) |
| Lisans | Apache-2.0 (kendi kodumuz). GPL bağımlılık **eklenmeyecek** |
| Python | **Sadece** `tools/freecad/` altında (FreeCAD headless script'leri). Uygulama ve core C# |
| Dağıtım | Self-contained + trimming; mümkün olan projelerde Native AOT. Hedef kurulum boyutu **< 80 MB** |
| Minimum sistem | Windows 10/11 x64, **4 GB RAM**, entegre GPU (Skia CPU fallback çalışmalı), 2 çekirdek |
| Performans hedefi | Soğuk açılış < 3 sn; 300 sayfalık projede açık sayfa dışı render yok; RAM < 300 MB (AI kapalı) |
| AI | Opsiyonel modül; kapalıyken hiç yüklenmez (düşük konfig etkilenmez) |
| UI paradigması | **Yaygın E-CAD araçlarına benzer kullanım alışkanlığı** (navigator'lar, dock'lanabilir paneller, özellik diyaloğu) — görsel kopya değil, iş akışı benzerliği |
| İsimlendirme | Hiçbir yerde rakip E-CAD ürünlerinin marka adı kullanılmayacak |
| Standart | IEC 81346 reference designation (=Fonksiyon +Yer -Ürün), IEC 60617 semboller |
| Üretici verisi | Uygulama üretici kütüphanesi **dağıtmaz**; kullanıcı import eder. Her kayıtta `source` + `license` alanı zorunlu |

---

## 1. Ajan Çalışma Kuralları

1. Her faz başında: kapsamı, etkilenen modülleri ve açık soruları yaz → **onay bekle**.
2. Faz içinde her iş kalemi ayrı commit. Fazlar arasında onaysız geçiş yok.
3. Her iş kalemi: kod + test + gerekiyorsa ADR. Testi olmayan iş "bitti" sayılmaz.
4. Core projesi UI'a, dosya sistemine, FreeCAD'e **bağımlı olamaz** (dependency kuralı test ile doğrulansın).
5. Spekülatif soyutlama yapma; sadece bu fazın ihtiyacı.
6. Teknik kararlar `docs/adr/NNNN-baslik.md` formatında.
7. Mevcut testleri kıran değişiklik merge edilmez.
8. Her faz sonunda `examples/` altındaki referans projeyle uçtan uca demo.

---

## 2. Solution Yapısı (hedef)

```text
aktapokus-electric/
├─ src/
│  ├─ Ake.Core/            # Domain model, Commands, ERC — saf C#, bağımlılıksız
│  ├─ Ake.Serialization/   # JSON proje formatı, şema versiyonlama, migration
│  ├─ Ake.Library/         # Komponent kütüphanesi (SQLite), importer'lar
│  ├─ Ake.Reports/         # BOM, kablo, terminal, I/O listeleri (CSV/XLSX)
│  ├─ Ake.Layout/          # Şema sayfa yerleşimi (auto-layout)
│  ├─ Ake.Render/          # Skia scene graph → ekran / PDF / SVG / DXF
│  ├─ Ake.Plc/             # Bus topolojisi, I/O adresleme; TwinCAT (AML/ESI/GVL), L5X, Machine Expert
│  ├─ Ake.Panel/           # 2D ray yerleşimi + FreeCAD köprüsü
│  ├─ Ake.Ai/              # LLM → Command patch, doğrulama, onay akışı
│  ├─ Ake.Cli/             # `ake` komut satırı aracı
│  └─ Ake.Editor/          # Avalonia uygulaması
├─ tests/
├─ tools/freecad/          # Python script'leri (headless)
├─ examples/ref-machine/   # Referans makine projesi
└─ docs/adr/
```

---

## Durum ve sıra (2026-11-17, proje sahibiyle kararlaştırıldı)

Aşağıdaki fazlardaki onay kutuları Faz 0–4 için işaretlenmeden kalmıştı; güncel durum bu tablodur.
Ayrıntı için ilgili ADR'lere bakılır.

| Faz | Durum |
|---|---|
| 0 İskelet · 1 Veri modeli · 2 Command + ERC · 3 Raporlar | Bitti |
| 4 Kütüphane | **Bitti** (2026-11-21). ESI → Faz 7, STEP referansı → Faz 8, BMEcat/ETIM eşlemesi, GSDML, EDS, IODD, AASX, deneysel EDZ `part.xml` ve SkyCAD içe aktarıcıları (**yapıldı — ADR-0059**) |
| 5 Şema çizimi | Bitti; devre tipine göre otomatik yerleşim (v1) kaldı |
| 6 Editör | **Bitti** (2026-11-18). Komut şeridi, tema, taşınabilir paneller, sekmeli sayfalar, durum çubuğu, makrolar, sürükle-bırak, çoklu seçim, kopyala/yapıştır, tel çekme (köşe noktası dahil) ve seçip silme, sekmeli özellik penceresi (ADR-0054: 5 sekme + görsel sembol seçimi + sağ tık kısayolları), çapraz referansa gitme, sayfa ağacı ve navigator'lar, kısayol profili, klemens şeridi düzenleyicisi, blok editörü şeridi, DXF/DWG'den sembol, PDF'ten düzenlenebilir içe aktarma, performans ölçümü, DoD (ADR-0023–0031) ve kalan model formları: yapı yönetimi, kablo/damar, potansiyel işareti (**yapıldı — ADR-0055**) |
| 7 PLC · 8 Pano + 3D · 9 Yapay zeka | Başlamadı |
| 10 Simülasyon | Aday |

**Devam sırası:**

1. **Faz 6'yı bitir**, şu sırayla:
   1. ~~Yeni proje, Farklı kaydet, kaydedilmemiş değişiklikle kapatırken uyarı~~ (yapıldı)
   2. ~~Teli seçip silme~~ (yapıldı); ~~tel çekerken köşe noktası~~ (yapıldı: ADR-0029; çekerken boş yere tıkla, sonradan sağ tık > Köşe noktaları ver)
   3. ~~Sekmeli özellik penceresi (tag, açıklama, ürün, yapı düğümü, bağlantı noktaları)~~ (yapıldı: Genel / Ürün / Noktalar)
   4. ~~Sağ tık menüleri; çapraz referansa tıklayınca hedef sayfaya gitme~~ (yapıldı: çift tık bağlantıya gider; sağ tık menüsü nesneye göre)
   5. ~~Sayfa ağacı (=/+) ve filtre~~ (yapıldı)
   6. ~~Klemens, PLC ve kablo navigator'ları~~ (yapıldı, alt panelde); ~~klemens şeridi düzenleyici~~ (yapıldı: ADR-0030; üst/alt kat ve sonlandırıcı modelde yok, kaldı)
   7. ~~Kısayol profili~~ (yapıldı: `docs/kisayollar.md`); ~~blok editörünün şerit görünümü~~ (yapıldı)
   8. ~~300 sayfalık sentetik projede performans ölçümü~~ (yapıldı: `docs/performans.md`, `tools/Ake.Bench`; hedefler sağlandı); DoD: referans makine boş projeden yeniden kuruldu (yapıldı: ADR-0031, komut düzeyinde)
2. **PacDrive DXF/DWG denemesi:** örnek `examples/dxf/pacdrive-lxm62.dxf`, `.dxf.zip` ve örnek Excel şablonları (`motors`, `islands`, `controller-io-blocks`, `driver-io-blocks`, SEE Electrical içe aktarma) **tamamlandı — ADR-0057**. Gerçek makine CAD arşivi `Ornek/` altına konduğunda aynı komutlarla doğrudan denenebilir.
3. **Faz 7 PLC:** Schneider (Machine Expert / PacDrive) → TwinCAT 3 → Studio 5000.
4. **Faz 5 kalanı, otomatik yerleşim v1** (PLC kartı sayfası Faz 7 ile birlikte).
5. **Faz 8 pano yerleşimi ve 3D.**
6. **Faz 9 yapay zeka** (yerel model).
7. **Faz 10 simülasyon** (aday).

Faz 6 sonrasına bırakılanlar (ADR-0057 / ADR-0058 ile tamamlandı; yalnızca grafik rapor sayfaları — klemens/kablo şeması, BOM sayfası — ile revizyon işaretleri sonrası kaldı): ~~makro kütüphanesi ve değer kümeleri, çok dilli metinler, koyu çizim zemini, SEE Electrical'dan içe aktarma, DXF'in sayfaya serbest grafik olarak alınması, kapak logosu ve proje önayarları~~ (**yapıldı — ADR-0057, ADR-0058**).

**Faz 6c — Numaralandırma, etiket, adres çapraz referansı (2026-11-18, proje sahibi: "en kritik konu"):**

1. ~~**Esnek tel numaralandırma**~~ (yapıldı: ADR-0041) — eski not: **Esnek tel numaralandırma:** başlangıç numarası, **kod (önek)**, adım ve basamak sayısı proje ayarıdır ("W", başlangıç 100, 3 hane → W100). İki komut:
   **yeni telleri numarala** (mevcut numaralara dokunmaz, kaldığı yerden devam eder: araya sayfa eklenince eski tel numaraları değişmez) ve
   **baştan numarala** (yalnızca kullanıcı isterse). Potansiyel bazlı ve sıralı şema korunur.
2. **Sayfa numaraları:** araya sayfa eklenince sonraki sayfalar yeniden numaralanmaz: 3. sayfadan sonra **3.1**, sonra 3.2… Çapraz referans, içindekiler,
   sayfa linkleri ve yer imleri alt sayfa numarasıyla uyumlu çalışır (çapraz referansta `sayfa.kolon` ayracı alt sayfada çakışmamalı).
3. **Kablo etiketi:** elektrik devre standardına uygun etiket içeriği (tel numarası; iki uçta hedef: `-K1:3 → -X1:1`; kablo/damar; renk; kesit) ve
   **etiket yazıcıları için çıktı** (CSV/XLSX `ake report labels`, ADR-0043 ve kesim kılavuzlu baskıya hazır **A4 etiket PDF'i**, **yapıldı — ADR-0056**).
4. ~~**PLC adresi ↔ tel numarası çapraz referans tablosu:**~~ (**yapıldı — ADR-0056**: `ReportCatalog.PlcCrossReference` / `plc-xref`, iki yönlü sıralı `PlcIoWindow` düzenleyici ve CSV PLC adres listesi içe aktarma).
5. **Yapay zeka ile değişiklik (Faz 9, öne alınıyor):** **yerel LLM** ile istem yazarak değişiklik (Command patch'i); dokümana uygun **RAG** ve dokümantasyon
   (komut şeması, ADR'ler, örnek projeler); örnek proje olarak proje sahibinin PDF'i incelenecek. Runtime, model ve donanım (32 GB RAM, GPU yok → CPU, 7B Q4): **ADR-0044 kabul edildi, ilk dilim yapıldı** (`Ake.Ai`, `ake ai`, editörde istem penceresi, model seçimi; gerçek modelle doğrulandı, `docs/yapay-zeka.md`). Sırada: doküman RAG, örnek PDF projeyle değerlendirme.
6. **Hazır sayfa şablonları: A4/A3/A2 yatay-dikey yapıldı (ADR-0049); kapak/içindekiler sayfa türleri ADR-0046** (A4/A3/A2, kapak, içindekiler, tek hat…) ve **pnömatik devre şeması** (semboller, hava hattı bağlantı türü, pnömatik uç türleri) — **ilk dilim yapıldı (ADR-0052, `examples/pnomatik`)**.

**Beckhoff çizimleri (2026-11-18):** `Industrial_PC` DXF'leri indirildi ve incelendi: mekanik montaj çizimleri, şema sembolü değil; Faz 8 (pano yerleşimi, `Mounting` çıkarımı) için saklanır — `docs/beckhoff-cizimleri.md`.

**Faz 6d — Standart E-CAD özellik eşitliği (2026-11-19, proje sahibi: "standart elektrik devre şeması programlarında olanlar bizde var mı? örneğin kapak sayfası"; kapsam onay bekliyor):**
Yaygın bir E-CAD programının proje oluşturma, proje özellikleri ve yeni sayfa pencereleriyle karşılaştırma (ekran görüntüleri). Eksikler:
1. **[Yapıldı: Kapak, İçindekiler, Malzeme listesi, Klemens planı, Kablo listesi (ADR-0046); Potansiyel özeti, Pin-bağlantı, PLC şeması (modül kutuları; ADR-0053), Revizyon özeti, Çerçeve dokümantasyonu (ADR-0050)]** Sayfa türleri** (Yeni sayfa penceresi): bizde yalnızca Şema, Haberleşme şeması ve Tek hat var. Eklenecekler: **Kapak sayfası**, **İçindekiler**, **Malzeme listesi**, **Klemens planı**, **Kablo listesi**, **Potansiyel özeti**, **Pin-bağlantı şeması**, **PLC şeması**, **Revizyon özeti**, **Çerçeve dokümantasyonu**. Raporlar şu an yalnızca CSV/XLSX; antetli gerçek sayfa olarak da üretilmeli (PDF/baskı, sayfa numarası ve içindekiler bağlantısı dahil).
2. **[Yapıldı — ADR-0055]** **Yeni sayfa penceresi ve sayfa özellikleri:** tam sayfa adı (=fonksiyon+yer/numara), sayfa türü seçimi, sayfa açıklaması, sayfa numarası, ölçek, ızgara, çerçeve (şablon) ve sayfa özellikleri diyaloğu.
3. **[Yapıldı — ADR-0047, ADR-0058]** **Proje oluşturma penceresi:** proje adı, saklama konumu, şablon, çoklu proje önayarı (`ProjectPreset`), oluşturma tarihi, oluşturan.
4. **[Yapıldı — ADR-0047]** **Proje özellikleri penceresi:** açıklama, sipariş/iş numarası, firma adı ve adresi, proje başlangıcı, oluşturan bilgileri, revizyon biçimi; sekmeler Özellikler, İstatistik, Yapı, Durum.
5. **[Yapıldı — ADR-0051]** **Sayfa gezgini:** özellik süzgeci (alan + değer), Ağaç/Liste görünümü, **grafik önizleme** paneli.

**Marka (2026-11-18):** tamamen sarı tema, logo, Hakkında ve açık kaynak bilgisi yapıldı (ADR-0038).

**Faz 6b — Komponent ve sembol altyapısı (2026-11-18, proje sahibi isteği; sıra: 2, 3, 1, 4, 5, 6):**
Standart bir E-CAD'de hazır gelen şeylerin elle yazılması beklenmez. Sıra ve kapsam onaylanınca ADR'lere bölünür.

1. ~~**Standart komponent paleti**~~ (yapıldı: ADR-0035; 41 sembol, varsayılan kütüphane otomatik) — eski not: **Standart komponent paleti:** uygulama açılınca varsayılan kütüphane otomatik açık; kategorili palet (kumanda, koruma,
   motor, güç, klemens, sensör, haberleşme…); çift tık / sürükle ile yerleştirme; yaklaşık 40–60 IEC tipi eleman.
2. ~~**Blok editörü uç özellikleri:**~~ (yapıldı: ADR-0033) uç türü (güç, sinyal, haberleşme), **NC/NO kontak ucu**, uç adı, cihaz adı alanı,
   bağlantı tipi (vidalı, M8, M12, RJ45/Ethernet, push-in…). Kullanıcı kendi ürünlerini yazabilmeli (yaz vd).
3. ~~**Çok katlı klemens (1–3 kat), çok bağlantılı klemens, PE klemensi, sonlandırıcı**~~ (yapıldı: ADR-0034, ADR-0040) — eski not: **Çok katlı / çok bağlantılı klemens:** 1–3 kat, çok bağlantılı klemens, sonlandırıcı, PE klemensi (ADR-0003/0030'daki "kaldı").
4. ~~**Hazır ürün kütüphanesi:**~~ (**yapıldı — ADR-0056**: `StandardProducts` 32 standart ürün, `ProductSelectionWindow` kategorili seçici / cihaza ürün atama / JSON-CSV içe aktarma) elektronik ekipmanlarda kullanılan çok sayıda ürün (sensör, M8/M12 konektörlü saha elemanı, Ethernet anahtarı, I/O, sürücü, güç kaynağı…); kullanıcı ekleyip içe aktarabilmeli.
4b. ~~**Bağlantı (kablo) türü, renk ve çizgi biçimi, otomatik gösterge**~~ (yapıldı: ADR-0036; haberleşme ve tek hat şemalarının temeli)
5. ~~**Haberleşme şeması**~~ (yapıldı: ADR-0037; **ADR-0057** ile `ake haberlesme --sayfa-ekle` ve `ake tekhat --sayfa-ekle` üzerinden projeye kalıcı sayfa olarak kaydedilir) — eski not: **Haberleşme şeması (bus topolojisi sayfası):** uçlara haberleşme türü verilince otomatik çıkar (Faz 7 bus topolojisiyle ortak).
   Proje sahibinin örneği (QElectroTech Profinet/Ethernet/DriveCLiQ topolojisi) gösterdikleri gereksinim olarak alındı:
   - **Kablo türü, renk ve çizgi biçimiyle** tanımlı (Profinet: düz yeşil, Ethernet HMI: kesik-noktalı koyu yeşil, DriveCLiQ: kesik gri);
     sayfada **gösterge (lejant)** otomatik üretilir; renk ve biçim tespit/okuma için bilgi taşır.
   - Kablo adı etiketi (`-WKF0.3`), cihaz kutusu, uç etiketleri (`P1`…`P6`, `X1 P1`), cihazın **IP adresi** (kırmızı), yapı grupları (`+SC1`, `+ST1`)
     çerçeve olarak.
   - Kablo/tel renk kodları hat şemalarına da yansır (tek hat şeması için de aynı bilgi kullanılır).
6. ~~**Tek hat şeması**~~ (yapıldı: ADR-0039; yalnızca güç hattı, antetli gerçek sayfa) — eski not: **Tek hat şeması:** güç dağıtımından otomatik üretim (kaynak → koruma → yük), Faz 5 otomatik yerleşimle birlikte.

---

## Faz 0 — İskelet

- [ ] Solution + proje yapısı, `Directory.Build.props`, nullable + warnings-as-errors
- [ ] Serilog altyapısı, global hata yönetimi (Result tipi, exception politikası ADR'si)
- [ ] CI (GitHub Actions): build + test + format kontrolü
- [ ] `CLAUDE.md`: mimari kurallar, komutlar, bu dosyaya referans
- [ ] Architecture test: `Ake.Core` başka hiçbir projeye referans vermiyor

**DoD:** `dotnet test` yeşil, CI çalışıyor.

---

## Faz 1 — Elektriksel Veri Modeli (en kritik faz)

- [ ] **Project / Structure:** IEC 81346 aspect'leri; `=` fonksiyon, `+` yer, `-` ürün hiyerarşisi
- [ ] **Device:** fiziksel ürün (ör. -K1, LC1D09). Üretici, ürün kodu, teknik özellikler
- [ ] **Function:** device'ın parçası (bobin A1/A2, NO 13/14, NC 21/22). Bir device → N function
- [ ] **Terminal / ConnectionPoint:** function'a ait, isimli bağlantı noktası
- [ ] **Connection:** iki ConnectionPoint arasındaki mantıksal bağ
- [ ] **Potential / Net:** bağlantılardan türetilen elektriksel eşpotansiyel küme (L1, 24VDC, 0V…). Otomatik hesaplanır
- [ ] **Wire:** fiziksel tel (kesit, renk, numara) — Connection'a bağlı
- [ ] **Cable / Core:** çok damarlı kablo ve damarları
- [ ] **TerminalStrip / TerminalBlock:** klemens grupları (-X1:1…n), köprüler
- [ ] **Mounting bilgisi (3D için şimdiden):** montaj tipi (DIN35 / plate / door), G×Y×D, clearance, ısı kaybı (W), STEP referansı
- [ ] **PLC I/O nesneleri — bus topolojisi olarak, rack/slot olarak değil:**
  - `Controller` (CPU / IPC) → `Network` (EtherCAT master, EtherNet/IP, Sercos…) → `Node` (coupler, box, sürücü) → `Module` (terminal, kart) → `Channel` → `ProcessDataEntry`
  - Node'lar arası port bağlantısı (EK1100 → EL… zinciri, EK1122 junction, segmentler)
  - Rockwell rack/slot ve Schneider yapısı bunun özel durumu olarak temsil edilir
  - Sinyal tipi (DI/DO/AI/AO), adres, sembolik değişken adı, açıklama
- [ ] **Graphical representation ayrı:** `Placement` (sayfa, konum, rotasyon) model nesnesinden bağımsız; bir function'ın birden fazla temsili olabilir (ana + referans görünüm)
- [ ] Stabil ID'ler (GUID) + insan-okunur tag'ler ayrı
- [ ] JSON serializer + şema versiyonu + round-trip testleri

**DoD:** `examples/ref-machine/` içinde elle yazılmış JSON ile gerçek bir makine (ana güç, 2 motor + kontaktör, 24VDC besleme, 1 PLC rack, 16 I/O, acil stop devresi) yüklenip kaydedilebiliyor; net hesabı doğru.

---

## Faz 2 — Command API + ERC

- [ ] `ICommand` (Execute / Undo), `CommandStack`, transaction (çoklu komut atomik)
- [ ] Temel komutlar: AddDevice, RemoveDevice, Connect, Disconnect, AssignWire, Rename/Retag, AssignIo
- [ ] **JSON patch formatı** (Command'ların serileştirilmiş hali) — AI ve import bunu kullanacak
- [ ] Audit log (kim / hangi kaynak: user / ai / import)
- [ ] **ERC kural motoru** (deterministik, eklentilenebilir):
  - Açık uçlu terminal
  - Kısa devre (farklı potansiyeller aynı net'te)
  - Tekrarlanan tag / tel numarası
  - Kesit vs koruma uyumsuzluğu (basit tablo)
  - Atanmamış I/O kanalı / çift atama
  - Function'ı olup device'ı olmayan (veya tersi) yetim nesneler
- [ ] Otomatik numaralandırma servisleri (device tag, tel numarası — kural tabanlı, potansiyel veya sayfa/kolon)
- [ ] `ake` CLI: `ake validate`, `ake renumber`, `ake apply patch.json`

**DoD:** CLI ile referans projeye patch uygulanıp ERC raporu alınabiliyor; undo/redo testleri tam.

---

## Faz 3 — Raporlar

- [ ] BOM (device bazlı, üretici/ürün koduna göre gruplu)
- [ ] Kablo listesi (kablo, damar, başlangıç–bitiş)
- [ ] Tel listesi / bağlantı listesi
- [ ] Terminal planı (klemens başına iç/dış bağlantı)
- [ ] PLC I/O listesi
- [ ] Çıktılar: CSV + XLSX. Rapor tanımları şablonlanabilir (kolon seçimi)
- [ ] `ake report bom|cables|terminals|io --format xlsx`

**DoD:** Referans projenin tüm raporları snapshot testleriyle doğrulanmış.

---

## Faz 4 — Komponent Kütüphanesi

- [ ] SQLite şeması: ürün, kategori, özellik (key/value + tipli), function şablonu, sembol, mounting, kaynak/lisans
- [ ] Kütüphane ürününden Device instantiate etme (function + terminal'ler otomatik)
- [ ] Importer'lar: CSV/JSON (toplu ürün), SVG/DXF (sembol), STEP referansı (dosya kopyalanmaz, kullanıcı yolu)
- [ ] **QElectroTech `.elmt` importer:** XML sembol → kendi sembol formatımız. Her elemanın `<informations>` alanındaki yazar/lisans bilgisi korunur; lisansı belirsiz veya CC-BY dışı eleman başlangıç setine alınmaz
- [ ] **Parametrik sembol üretici:** kontak (NO/NC), bobin, klemens, sigorta, motor, PLC kanal sembolleri kural tabanlı üretilir (elle çizim yok)
- [ ] **EDZ importer (deneysel, kullanıcı tarafı):** kullanıcının kendi indirdiği `.edz` arşivinden `part.xml` ürün verisi (ürün kodu, özellikler, function şablonu) okunur. Makro grafikleri hedef değil. Dosya içeriği uygulama ile dağıtılmaz
- [ ] **Üretici açık veri importer'ları** (hepsi kullanıcının indirdiği dosyadan, plug-in mimarisiyle):

  | Format | Kaynak | Ne verir | Öncelik |
  |---|---|---|---|
  | **ESI** (XML) | EtherCAT cihaz üreticileri (Beckhoff vb.) | Modül, kanal, PDO | 1 |
  | **GSDML** (XML) | PROFINET cihazları (Siemens vb.) | Modül/submodül, I/O yapısı | 2 |
  | **EDS** (INI benzeri metin) | EtherNet/IP cihazları (Rockwell vb.) | Assembly, I/O boyutu | 2 |
  | **IODD** (XML) | IO-Link sensör/aktüatörler (IODDfinder) | Process data, parametreler | 3 |
  | **BMEcat + ETIM** (XML) | Üretici ürün katalogları | Ürün kodu, açıklama, sınıflı teknik özellik | 1 |
  | **AAS / AASX** | Asset Administration Shell destekleyen üreticiler | Dijital isim plakası, teknik veri, CAD referansı | 3 (geleceğe hazırlık) |
  | **EDZ** `part.xml` | Üretici sitelerinden indirilen arşiv | Ürün verisi + function/terminal şablonu | deneysel |
  | **STEP / DXF** | Üretici ürün sayfaları | 3D gövde, 2D ön görünüş | 1 (Faz 8 için) |

- [ ] **ETIM sınıfı → function şablonu eşlemesi:** ör. kontaktör sınıfı → bobin (A1/A2) + ana kontaklar (1-2/3-4/5-6) + yardımcı kontaklar. Katalog verisinde terminal numarası olmadığında şablon devreye girer; kullanıcı düzeltebilir
- [ ] Her import kaydı: kaynak dosya, üretici, tarih, lisans notu (`source`, `license` alanları)
- [ ] Başlangıç seti: QET kaynaklı CC-BY semboller + parametrik semboller + Schneider/Rockwell/Siemens/Omron/Pilz/Festo için **elle girilmiş** örnek ürün kayıtları (sadece public teknik veri)
- [ ] `ake lib import|search|show`

**DoD:** LC1D09 kütüphaneden eklenince A1/A2, 13/14, 21/22 otomatik oluşuyor.

---

## Faz 5 — Şema Render + Auto-Layout (salt-okunur)

- [x] Sayfa modeli: çerçeve, antet (title block), kolon/satır grid'i (çapraz referans için)
- [x] Sembol render (Skia), tel çizimi (ortogonal routing)
- [x] **Çapraz referanslar:** bobin altında kontak ayna tablosu; sayfalar arası potansiyel oku
- [ ] Auto-layout v1: devre tipi bazlı şablonlar (güç dağıtımı, motor çıkışı, PLC I/O kartı sayfası) — **kalan:** şu an yalnızca genel yerleşim (v0) var
- [x] Çıktı: PDF (çok sayfalı, içindekiler, bookmark), SVG, DXF, DWG (ADR-0020, ADR-0022)
- [x] Sayfa bazlı tag şeması (ADR-0021)
- [x] `ake render --pdf`

**DoD:** Referans projeden okunabilir, standart görünümlü bir şema PDF'i otomatik üretiliyor. **Bu, MVP dokümantasyon hedefinin gerçekleştiği noktadır.**

---

## Faz 6 — Etkileşimli Editör (Avalonia)

### Yerleşim (yaygın E-CAD alışkanlığına uygun)
- [ ] Dock altyapısı: **Dock** kütüphanesi (Avalonia, MIT) — panel konumları kullanıcı profiline kaydedilir
- [ ] Orta alan: sekmeli grafik editör (birden fazla sayfa açık)
- [ ] Sol: **Sayfa navigator** (=/+ yapısına göre ağaç + liste görünümü, filtre)
- [ ] Sol/sağ: **Device navigator** (tag'e göre; yerleştirilmiş / yerleştirilmemiş function'lar ayrı gösterilir, navigator'dan sayfaya sürükle-bırak)
- [ ] **Potansiyel navigator**, **Klemens navigator**, **PLC navigator**, **Kablo navigator**
- [ ] Üst: menü + toolbar (ribbon Faz 6 sonrası; Avalonia'da olgun ücretsiz ribbon olmadığı için önce toolbar)
- [ ] Alt: mesaj yönetimi (ERC sonuçları, tıkla → nesneye git), durum çubuğunda koordinat/grid
- [ ] **Özellik diyaloğu** (çift tık): sekmeli — tag, function, ürün, bağlantı noktaları, görüntüleme
- [ ] **Kısayol profili:** yapılandırılabilir keymap; E-CAD kullanıcısına tanıdık varsayılan set (dokümante edilmiş JSON)
- [ ] Sağ tık bağlam menüleri, sayfa arası atlama (çapraz referansa tıkla → hedef sayfa)

### Fonksiyonlar
- [ ] Proje gezgini (yapı ağacı: =/+/- ve sayfalar)
- [ ] Canvas: zoom, pan, grid, snap, seçim, çoklu seçim
- [ ] Kütüphaneden sürükle-bırak sembol yerleştirme → `AddDevice` command
- [ ] Tel çekme → `Connect` command; terminal snap
- [ ] Taşıma / döndürme yalnızca `Placement` değiştirir (bağlantı korunur — test)
- [ ] Özellik paneli, ERC sonuç paneli (tıkla → nesneye git)
- [ ] Undo/redo, kopyala/yapıştır (yeni tag atamalı)
- [ ] Auto-layout ile üretilmiş sayfayı elle düzenleme

- [x] Performans: sayfa sanallaştırma (sadece görünen sayfa render), navigator'larda sanal liste, büyük projede lazy load (ölçüm: `docs/performans.md`)
- [ ] Düşük konfig testi: 4 GB RAM sanal makinede referans proje + 300 sayfalık sentetik proje benchmark'ı CI'da

**DoD:** Referans proje editörde sıfırdan çizilebiliyor ve aynı raporları veriyor; performans hedefleri (Bölüm 0) sağlanıyor.

**Durum (2026-11-18): sağlandı.** Referans makine boş projeden yalnızca komutlarla kuruldu ve beş raporu, ERC sonucunu ve beş sayfanın çizimini referansla birebir verdi (ADR-0031, `ReferenceMachineRebuildTests`); performans hedefleri sağlandı (`docs/performans.md`). DoD komut düzeyindedir: yapı düğümü, kablo/damar, potansiyel işareti ve PLC topolojisi için editör formları yoktur (ADR-0031 "Yapılmayanlar"; PLC düzenleyicisi Faz 7).

---

## Faz 7 — PLC Köprüsü (farklılaştırıcı)

> **Uygulama sırası:** Bu faz Faz 4'ün (kütüphane) hemen ardından yapılır; editöre bağımlı değildir. Öncelik **TwinCAT 3**, sonra Studio 5000, sonra Machine Expert.

### 7A — TwinCAT 3 (birincil hedef)

- [ ] **ESI importer:** Beckhoff ESI XML dosyalarından (kullanıcı indirir) terminal tanımları → kütüphane: kanal sayısı, PDO yapısı, ürün kodu, revizyon. Dosya dağıtılmaz
- [ ] **AutomationML (AML) export:** EtherCAT topolojisi (master → coupler → terminal, port bağlantıları) + kanal ↔ değişken eşleşmesi. TwinCAT'in AML data exchange import'u ile uyumlu
- [ ] **GVL üretimi:** `AT %I*` / `AT %Q*` değişkenleri, sembolik isim = device tag + function, açıklama = sinyal metni
- [ ] **Automation Interface aracı (opsiyonel, Windows):** `tools/twincat/` altında küçük bir C# console; COM üzerinden I/O ağacını oluşturur ve değişkenleri link eder (AML yolunun yedeği)
- [ ] **Geri yön (round-trip):** TwinCAT'ten AML export → elektrik projesiyle diff (devreye almada değişen terminaller)
- [ ] Sürücüler: EL72xx / AX5000 / AX8000 gibi eksen modüllerini Node olarak destekle; motor ↔ sürücü ↔ eksen ilişkisi modelde dursun

**DoD:** Referans projeden üretilen AML, TwinCAT 3 XAE'ye import edildiğinde I/O ağacı doğru oluşuyor ve GVL değişkenleri kanallara bağlı geliyor.

### 7B — Diğer platformlar

- [ ] I/O adresleme profilleri (Rockwell / Schneider)
- [ ] Export: **Studio 5000 L5X** (tag + modül), **Machine Expert** (GVL / IO mapping)
- [ ] Import: mevcut tag listesinden I/O ataması (CSV / L5X)
- [ ] Değişiklik raporu (elektrik projesi ↔ PLC projesi diff), tüm platformlar için ortak

**DoD:** Referans projeden üretilen L5X, Studio 5000'e hatasız import ediliyor.

---

## Faz 8 — Pano Yerleşimi ve 3D

- [ ] Pano modeli: kabin (G×Y×D), montaj plakası, DIN raylar, kablo kanalları, kapı
- [ ] **2D auto-layout (constraint-based packing):**
  - Ray doluluk, kanal boşlukları
  - Güç / sinyal bölge ayrımı
  - Kapı komponentleri (buton, HMI, lamba) ayrı
  - Isı yükü toplamı ve basit uyarı
- [ ] 2D pano görünümü + DXF/PDF çıktı (delik/kesim planı dahil)
- [ ] **Uygulama içi 3D önizleme (hafif):** Silk.NET/OpenGL ile kabin, ray, kanal ve komponent bounding box'ları; STEP mesh'i opsiyonel. FreeCAD gerektirmez
- [ ] **FreeCAD köprüsü (opsiyonel harici bileşen):** kullanıcının makinesinde FreeCAD kuruluysa `tools/freecad/build_panel.py` arka planda çalışır — JSON girdi → `.FCStd` + birleşik STEP çıktı. Kurulu değilse uygulama normal çalışır, sadece "STEP montaj export" pasif olur
- [ ] (Sonraki adım) FreeCAD'siz doğrudan STEP AP214 montaj dosyası yazımı (dış STEP referanslı) — değerlendirilecek
- [ ] Tel boyu tahmini (kanal üzerinden manhattan routing) → tel listesine yazılır
- [ ] `ake panel layout`, `ake panel export --step`

**DoD:** Referans projeden tek komutla 3D pano STEP'i üretiliyor, SolidWorks'te açılabiliyor.

---

## Faz 9 — AI Mühendislik Asistanı

- [ ] Provider soyutlaması: Ollama (yerel, varsayılan) + harici API (opsiyonel)
- [ ] LLM çıktısı **yalnızca JSON patch** (Faz 2 formatı); şema doğrulaması + ERC'den geçmeyen patch reddedilir
- [ ] Onay UI'ı: diff görünümü, kısmi kabul, kaynak = `ai` audit kaydı
- [ ] Yetenekler (sırayla): ERC sonuçlarını açıklama → metinden I/O listesi → metinden devre taslağı → komponent önerisi (kütüphane içinden, RAG)
- [ ] Bağlam üretimi: proje modelinin LLM için özet/serileştirilmiş görünümü
- [ ] MCP server (opsiyonel): Claude Code / harici ajanların `ake` core'una erişimi

**DoD:** "3 motorlu konveyör, her motor için kontaktör + termik, PLC'den kontrol" metninden onaylanabilir, ERC-temiz bir patch üretiliyor.

---

## Faz 10 (aday) — Simülasyon

Proje sahibi yapay zekadan sonra bir simülasyon katmanı düşünüyor (2026-11-16). Kapsam henüz
belirlenmedi; adaylar:

- Kontrol devresi mantık simülasyonu: anahtar/buton durumları → bobin, kontak, potansiyel durumu; şemada canlı renklendirme.
- PLC ile birlikte sanal devreye alma: PLC I/O'sunun (Faz 7) sanal bir süreç ya da sahayla eşlenmesi.
- Elektriksel hesaplar: kablo kesiti, gerilim düşümü, kısa devre ve koruma seçiciliği kontrolü.

Model tek doğruluk kaynağı olduğu için simülasyon da modelin bir istemcisidir; modeli değiştirmez.
Faz başında kapsam proje sahibiyle netleştirilir.

---

## Ek Gereksinimler (2026-11-15, henüz fazlara dağıtılmadı)

Proje sahibinin Faz 1 sırasında eklediği gereksinimler. Her biri ilgili fazın planında ele alınır;
"karar bekliyor" olanlar o faz başlamadan önce ADR ile kapatılır.

- **Masaüstü uygulaması zorunlu.** CLI tek başına ürün değildir; `Ake.Editor` (Faz 6) kapsamdan çıkarılamaz.
- **PLC üreticisi sınırlanmaz.** Beckhoff, Schneider ve Rockwell'in hepsi desteklenir; model üreticiden
  bağımsızdır (ADR-0003). Faz 7'deki sıra yalnızca uygulama önceliğidir.
- **Kendi kaynak formatı.** Proje kendi JSON formatında saklanır (ADR-0001, ADR-0003).
- **Çıktı: PDF, DXF ve DWG.** Üçü de Faz 5'te var. DWG için ACadSharp (MIT) seçildi (ADR-0022).
- **DXF / DWG içe aktarma (editörde düzenlenebilir).** Faz 4'teki sembol import'undan farklıdır:
  bütün bir çizim sayfaya alınır. İçe alınan çizim yalnızca grafiktir, elektriksel anlam taşımaz;
  bunun için modelde "serbest grafik" nesnesi gerekir (Faz 5/6). DWG tarafı yukarıdaki karara bağlı.
- **Başka E-CAD araçlarından proje aktarma (diğer E-CAD araçları, SEE Electrical).** Ek özellik. **Araştırma
  gerekiyor:** bu araçların yerel proje formatları kapalıdır; gerçekçi yol, onların dışa aktardığı
  dosyalardan (DXF/DWG, XML, liste çıktıları) okumaktır. Grafikten elektriksel modeli geri kurmak
  (sembol ve bağlantı tanıma) ayrı ve büyük bir iştir. **Karar bekliyor:** Bölüm 0'daki "hiçbir
  yerde rakip marka adı kullanılmayacak" kuralı, içe aktarma menüsünde kaynak formatı adlandırmayı
  da kapsıyor mu?

### Örnek proje (`Ornek/`, depoya girmez)

Proje sahibi, gerçek bir makine projesini hedef olarak koydu: başka bir E-CAD aracının proje dosyası, onun PDF çıktısı ve PDF'ten elle
üretilmiş Excel listeleri. **Hedef: bunların hepsini bu uygulamayla üretebilmek.** Dosyalar müşteri verisi içerdiği için `.gitignore`'dadır
ve bu depoda müşteriye ait hiçbir ad, belge ya da içerik bulunmaz; aşağıda yalnızca genel gereksinimler yer alır.

- **Antet ve sayfa şablonu zorunlu.** Kullanıcı kendi antetini ve sayfa şablonunu oluşturabilmeli (çerçeve, antet alanları, logo, proje
  bilgisi yer tutucuları). Faz 5'teki "sayfa modeli" maddesi sabit bir antet değil, düzenlenebilir şablon olarak ele alınır; şablon editörü Faz 6'ya girer.
- **Excel listeleri (Faz 3 kapsamına eklenir):** motor/redüktör listesi (motor tipi, redüktör, sürücü tipi ve adı, bus adresi, güç), ada listesi
  (valf adaları ve sensörler), kontrolör I/O listesi ve sürücü I/O listesi. Faz 3'teki rapor şablonları bu dört listeyi üretebilmelidir.
- **PLC platformu:** örnek proje bir hareket kontrol platformu (Sercos ve CANopen sürücüleri) kullanır. Model bunu taşıyor (ADR-0003); Faz 7'deki
  uygulama sırası bu örneğe göre **yeniden değerlendirilmeli**.
- **Kaynak proje dosyası parola korumalıdır.** Parolayı aşmak denenmeyecek. İçe aktarma yolu, kaynak aracın kendi dışa aktarma çıktılarıdır
  (DXF/DWG, liste dışa aktarımı). Hangi çıktıların alınabildiği proje sahibinden öğrenilecek.
- **Örnek sayfanın yapısı** (genel özellikler; Faz 5 bunları üretebilmelidir):
  - **Çerçeve:** üstte kolon numaraları, yanlarda satır harfleri (ızgara).
  - **Antet (alt şerit):** solda firma logosu; sayfa açıklaması; proje, tarih, çizim no, fonksiyon, yer, çizen, revizyon, sayfa, toplam sayfa, sonraki sayfa alanları.
  - **Çapraz referans biçimi `sayfa.kolon`:** potansiyel hatları sayfanın solundan geldiği ve sağından gittiği yeri gösterir; sinyal okları hedef adı ve
    hedef konumla biter.
  - **Tel numaraları telin üzerinde** yazılıdır; tel rengi çizgi rengidir (N ve 0 V mavi, PE yeşil-sarı kesikli, 24 V turuncu).
  - **Sayfa bazlı tag şeması:** tag'ler sayfa numarasıyla başlar (sayfa, harf, o sayfadaki sıra). Bu, IEC 81346'nın `=`/`+`/`-` yazımından farklıdır.
    Model bunu taşır (tag serbest metin), ama numaralandırma servisi ve tag gösterimi şu an yalnızca "harf + sayı" biçimini bilir. **Faz 5'te
    "sayfa + harf + sıra" şeması eklenmeli** ve hangi şemanın kullanılacağı proje ayarı olmalıdır.
  - **Çok kutuplu cihaz gösterimi:** elektronik sigorta gibi cihazlar tek kutu içinde birden çok kanal olarak çizilir (kanal başına akım değeri).
- **I/O listesi düzeni:** modül başına yan yana bloklar; her blokta `Klemens | Nr | İsim | Kod` sütunları (Klemens: sinyalin geçtiği klemens, Nr: kanal,
  İsim: sembolik ad, Kod: sahadaki eleman). Faz 3 I/O raporu bu dört bilgiyi verir; yan yana blok yerleşimi rapor şablonuna bırakılmıştır.

### Yapay zeka entegrasyonu şart (2026-11-15)

Proje sahibi: "Yapay zeka entegrasyonu şart; basit bir yerel LLM işimizi görür. Temel her şey
bittikten sonra, yapılacak değişiklikleri LLM'e yazıp devre şeması oluşturma işini yapmalıyız."

- Bu, **Faz 9**'un kendisidir ve sırası değişmedi: önce temel (Faz 5–8), sonra AI. Değişen şey
  önceliği: Faz 9 "olsa iyi olur" değil, ürünün zorunlu parçasıdır.
- ADR-0001'deki "AI opsiyonel modül" satırı şöyle okunur: özellik **mutlaka vardır**, ama modül
  kapalıyken yüklenmez; böylece düşük donanımlı makinede uygulamanın geri kalanı etkilenmez.
- **Altyapının hazır olan kısmı** (Faz 2 ve 4): LLM modeli doğrudan değiştirmez, yalnızca patch
  üretir; patch nesnelere tag ile atıf yapar (GUID uydurmak gerekmez); ürünler sipariş koduyla
  kütüphaneden gelir (uç adlarını LLM'in bilmesi gerekmez); patch ya tamamen uygulanır ya hiç;
  ERC'den geçmeyen patch `--strict` ile reddedilir; her değişiklik `source: ai` ile audit'e yazılır.
- **Faz 9 planında ele alınacak riskler:**
  - Küçük yerel modeller geçerli, şemaya uygun JSON üretmekte zorlanır. Çıktı JSON şemasıyla
    kısıtlanmalı ve "üret → doğrula → hatayı modele geri ver → yeniden dene" döngüsü kurulmalı.
  - Yerel LLM belleği, ADR-0001'deki 4 GB RAM alt sınırıyla bağdaşmaz. AI için ayrı bir asgari
    donanım tanımlanmalı; harici API seçeneği (yol haritasında var) düşük donanım için yedektir.
  - "Devre şeması oluşturma" iki adımdır: elektriksel model (patch) ve çizim. Çizimi LLM yapmaz;
    Faz 5'teki auto-layout yapar. Bu yüzden auto-layout'un kalitesi AI özelliğinin de sınırıdır.

### Sembol kütüphaneleri, blok editörü ve arayüz (2026-11-16)

Proje sahibi: QElectroTech ve SkyCAD'in hazır sembol kütüphaneleri entegre edilsin; TraceParts,
PARTcommunity/CADENAS gibi sitelerden Schneider PacDrive, Rockwell ve Beckhoff'un DXF/DWG kütüphaneleri
eklenebilsin; mutlaka bir blok editörü olsun; çizim alanında ızgara olsun; arayüz, kullanıcı
alışkanlıklarını değiştirmemek için yaygın E-CAD düzenine benzesin.

- **Temel hazır (ADR-0013):** sembol tanımı veri olarak tutuluyor, projede saklanıyor, function'a
  atanabiliyor ve çiziliyor. İçe aktarıcılar ve blok editörü bunun üzerine yazılacak.
- **QElectroTech:** `.elmt` dosyaları XML'dir; içe aktarıcı yazılabilir. Makinede kurulu değil; sembol
  koleksiyonunun lisansı içe aktarmadan önce doğrulanacak. Her elemanın `<informations>` alanındaki
  yazar/lisans bilgisi sembolün `source`/`license` alanına taşınacak (Faz 4 maddesiyle aynı).
- **QElectroTech koleksiyonu denendi (ADR-0016):** 4.459 sembol alınıyor. Koleksiyon lisansı makine
  öğrenmesi eğitiminde kullanımı yasaklıyor; Faz 9'da semboller modele eğitim verisi olarak verilmeyecek.
- **SkyCAD:** dosya biçimi ve lisansı bilinmiyor; proje sahibinden örnek dosya bekleniyor.
- **TraceParts, PARTcommunity/CADENAS:** bu siteler hesap ve kullanım koşulu onayı istiyor; indirmeyi
  proje sahibi yapar. Dosyalar uygulamayla dağıtılmaz (ADR-0001). Yapılacak iş, kullanıcının indirdiği
  DXF/DWG'yi sembole (2D ön görünüş) ya da pano görünüşüne çeviren içe aktarıcıdır. **Sembole çeviren
  içe aktarıcı yapıldı (ADR-0027, DXF ve DWG);** gerçek PacDrive dosyasıyla deneme bekliyor.
- **Blok editörü:** sembol çizme ekranı. Öğe ekleme/taşıma/silme, uç yerleştirme ve yönü, ızgaraya
  yapışma, kaydetme (`*.sembol.json`). Model ve dosya biçimi hazır; ekran yapılmadı.
- **Izgara:** editörde yapıldı (ADR-0013).
- **Arayüz düzeni karara bağlandı (2026-11-16):** proje sahibi yaygın E-CAD düzenini seçti (ADR-0014).

### Verilen kararlar (2026-11-15)

Proje sahibi, yukarıda "karar bekliyor" denen konularda en uygun seçeneğin seçilip devam
edilmesini istedi. Seçilenler aşağıda; her biri ilgili fazın planında ADR'ye dönüşür ve o noktada
değiştirilebilir.

| Konu | Karar | Kesinleşeceği yer |
|---|---|---|
| DWG | **Uygulandı** (çıktı ve sembol olarak içe aktarma, ADR-0027). ACadSharp (MIT) denendi ve yeterli bulundu; ADR-0022 ADR-0001'in "DWG desteklenmeyecek" satırının yerini aldı. | ADR-0022 |
| Marka adı kuralı | Ürün adında, arayüz kimliğinde ve tanıtımda başka ürünlerin marka adı kullanılmaz. Tek istisna: içe aktarma işlevinde ve belgelerde **kaynak dosya biçimini** adlandırmak (ör. "SEE Electrical dışa aktarımı"). | İçe aktarma işinin planı |
| Başka E-CAD araçlarından aktarma | Yerel proje dosyaları okunmaz (kapalı ya da parola korumalı). Yol, o araçların dışa aktardığı DXF/DWG ve liste dosyalarıdır. Çizimden elektriksel modeli tanıma ayrı bir iş kalemidir ve Faz 6'dan sonraya bırakılır. | İçe aktarma işinin planı |
| Faz 7 sırası | Örnek proje Schneider PacDrive olduğu için uygulama sırası **Schneider (Machine Expert / PacDrive) → TwinCAT 3 → Studio 5000** olur. Model üç platformu da taşıyor. | Faz 7 planı |

Proje sahibinin yapması gereken haricî eylem: **GitHub deposunu** açıp CI'ı ilk kez koşturmak (`.github/workflows/ci.yml` içine `Ake.Bench -- 300 --verify` bütçe doğrulaması eklendi). Derleme tarafında `Directory.Build.props` kurulu .NET 9 veya .NET 10 SDK'sını otomatik seçer; hedef makinede ek SDK kurulumu gerekmez.

---

## İlk Oturum Talimatı (Claude Code'a yapıştır)

```text
ROADMAP.md'yi oku. Bölüm 0'daki kararları ADR-0001 olarak yaz.
Faz 0'ı planla: oluşturacağın projeleri, paketleri ve CLAUDE.md içeriğini listele.
Kodlamaya başlamadan önce planı onayıma sun.
Ardından Faz 1 veri modeli için sınıf diyagramı (Mermaid) ve açık soruları hazırla —
özellikle Device/Function/Potential ayrımı ve Placement bağımsızlığı.
```
