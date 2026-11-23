using System.Text;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;

namespace Ake.Editor;

/// <summary>
/// Tek bir yardım kılavuzu bölümü: Türkçe/İngilizce içerik, adım adım yönerge, kısayollar, ipuçları ve vektörel şematik diyagram.
/// </summary>
public sealed record HelpTopic(
    string Id,
    string TitleTr,
    string TitleEn,
    string SummaryTr,
    string SummaryEn,
    IReadOnlyList<string> StepsTr,
    IReadOnlyList<string> StepsEn,
    IReadOnlyList<string> Shortcuts,
    IReadOnlyList<string> TipsTr,
    IReadOnlyList<string> TipsEn,
    string Keywords,
    Func<UiLanguage, Scene> BuildIllustration)
{
    public string TitleFor(UiLanguage lang) => lang == UiLanguage.English ? TitleEn : TitleTr;

    public string SummaryFor(UiLanguage lang) => lang == UiLanguage.English ? SummaryEn : SummaryTr;

    public IReadOnlyList<string> StepsFor(UiLanguage lang) => lang == UiLanguage.English ? StepsEn : StepsTr;

    public IReadOnlyList<string> TipsFor(UiLanguage lang) => lang == UiLanguage.English ? TipsEn : TipsTr;

    public string SearchCorpus(UiLanguage lang) => ComponentLibrary.FoldText(string.Join(
        ' ',
        [
            TitleFor(lang),
            TitleTr,
            TitleEn,
            SummaryFor(lang),
            .. StepsFor(lang),
            .. Shortcuts,
            .. TipsFor(lang),
            Keywords,
        ]));
}

/// <summary>
/// Uygulama içi Yardım penceresi ve tek dosyalık HTML yardım kılavuzu (<c>yardim.html</c>) için
/// aranabilir, görselli ve iki dilli (TR / EN) içerik kaynağı.
/// </summary>
public static class HelpContent
{
    private const string Font = "Arial";

    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new(
            Id: "calisma-alani",
            TitleTr: "1. Çalışma Alanı, Paneller ve Gezinme",
            TitleEn: "1. Workspace, Panels & Navigation",
            SummaryTr: "Ana pencere üstte komut şeridi (Ribbon), solda Sayfalar ve cihaz/sembol panelleri, ortada sekmeli çizim tuvali ve altta anlık ERC durum çubuğundan oluşur.",
            SummaryEn: "The main window arranges the command Ribbon on top, dockable navigators on the sides, tabbed schematic sheets in the center, and a live status + ERC bar at the bottom.",
            StepsTr:
            [
                "Dosya > Yeni ile boş bir proje veya örnek pano oluşturun; Aç ile mevcut .akp / .eproj dosyasını yükleyin.",
                "Sol taraftaki Sayfalar panelinden sayfaya çift tıklayarak çizim sekmesini açın.",
                "Fare tekerleği ile yakınlaşın, boş alana basılı tutup sürükleyerek sayfayı kaydırın.",
                "Görünüm > Tam çizim alanı (F11) ile yan panelleri gizleyip yalnızca şemaya odaklanın.",
                "Görünüm > Varsayılan düzen komutu taşınan veya kapatılan tüm panelleri başlangıç yerine döndürür.",
            ],
            StepsEn:
            [
                "Start from File > New or open an existing .akp / .eproj project with File > Open.",
                "Click a page in the Pages navigator to open its schematic tab in the central workspace.",
                "Zoom with the mouse wheel around the cursor and drag empty paper to pan smoothly.",
                "Toggle View > Full Canvas (F11) to hide panels and maximize drawing space.",
                "Use View > Reset Layout anytime to restore docked panels to their defaults.",
            ],
            Shortcuts: ["Home", "G", "F11", "Ctrl+F1", "Ctrl+S"],
            TipsTr:
            [
                "Alt çubuğun en sağındaki 'ERC temiz / Hata' göstergesine dikkat edin; çift tıklayarak Mesajlar panelindeki ilgili cihaza atlayabilirsiniz.",
                "Koyu zemin düğmesi uzun çizim oturumlarında göz yorgunluğunu azaltır.",
            ],
            TipsEn:
            [
                "Check the 'ERC clean / Error' indicator on the bottom-right; double-click items in Messages to jump straight to the element.",
                "Enable View > Dark Canvas for high-contrast low-glare drafting.",
            ],
            Keywords: "workspace ribbon panel dock zoom pan grid dark status erc layout çalışma alanı şerit panel yakınlaş",
            BuildIllustration: BuildWorkspaceIllustration),

        new(
            Id: "sayfa-yonetimi",
            TitleTr: "2. Sayfalar, Alt Sayfalar (3.1) ve Sürükle-Bırak Sıralama",
            TitleEn: "2. Pages, Subpages (3.1) & Drag-Drop Reordering",
            SummaryTr: "Sayfaları fonksiyon/konum ağacı veya düz liste görünümünde yönetebilir, mevcut iki sayfa arasına 3.1 gibi alt sayfa ekleyebilir ve sürükle-bırak ile sırasını değiştirebilirsiniz.",
            SummaryEn: "Switch between Tree and List views, insert decimal subpages such as 3.1 between consecutive sheets, and drag-and-drop pages to reorder with confirmation.",
            StepsTr:
            [
                "Sayfa > Yeni sayfa ile şema, kapak veya klemens planı sayfası ekleyin.",
                "İki sayfa arasına yeni sayfa sokmak için Sayfa > Araya sayfa ekle (3.1) komutunu veya sayfa ağacındaki sağ tık menüsünü kullanın.",
                "Sayfa gezgininde 'Ağaç' görünümü fonksiyon/konum klasörlerine göre gruplar; 'Liste' ise proje sırasını düz gösterir.",
                "Bir sayfayı fareyle tutup başka bir sayfanın üzerine sürükleyerek sırasını taşıyın; taşıma öncesi onay sorulur.",
                "Üst arama kutusu yanındaki alandan (Tümü / Numara / Başlık / Fonksiyon / Yer) hızlı süzme yapabilirsiniz.",
            ],
            StepsEn:
            [
                "Add schematic, cover, or terminal sheets with Page > New Page.",
                "Insert a subpage like 3.1 using Page > Insert Subpage (3.1) or the right-click context menu.",
                "Toggle between Tree (grouped by function/location) and List (linear order) at the bottom of Pages.",
                "Drag a page row onto another page to reorder it; a confirmation prompt protects against accidental drops.",
                "Filter pages by All, Number, Title, Function, Location, or Template via the dropdown.",
            ],
            Shortcuts: ["PageUp", "PageDown"],
            TipsTr:
            [
                "Numaralama karmaşıklaştığında Sayfa > Sayfa numaralarını baştan ver ile temiz sıralama üretebilirsiniz.",
                "Sayfalar panelinin altındaki önizleme kutusu tıkladığınız sayfanın küçük şemasını açmadan gösterir.",
            ],
            TipsEn:
            [
                "Use Page > Renumber Pages to normalize gaps after heavy editing.",
                "Click any page row to preview its schematic thumbnail without opening a new tab.",
            ],
            Keywords: "sayfa alt sayfa 3.1 ağaç liste sürükle bırak sırala numara page subpage tree list reorder",
            BuildIllustration: BuildPageManagementIllustration),

        new(
            Id: "kutuphane-surukle",
            TitleTr: "3. Ürün & Sembol Kütüphanesi (Sürükle-Bırak ve Düzenleme)",
            TitleEn: "3. Product & Symbol Library (Drag-Drop & Editing)",
            SummaryTr: "Kategoriler arasında sembolleri sürükle-bırak ile doğru klasöre taşıyabilir, yeni kategori açabilir, ürünleri doğrudan sayfaya sürükleyip yerleştirebilir ve sembol adını/grafiğini düzenleyebilirsiniz.",
            SummaryEn: "Move symbols across category folders via drag-and-drop, create custom categories, drag products or symbols onto blank pages, and edit names, metadata or geometry anytime.",
            StepsTr:
            [
                "Ekle > Ürün kütüphanesi… ile üretici ve sipariş koduna göre arama yapın, önizlemeden tüm uç etiketlerini kontrol edin.",
                "Bir sembolü veya ürünü listeden tutup doğrudan çizim tuvaline sürükleyip bırakın ya da 'Sayfaya yerleştir' düğmesine tıklayın.",
                "Yanlış kategoride duran bir sembolü tutarak sol taraftaki istediğiniz kategori klasörünün üstüne sürükleyip taşıyın; '+' düğmesiyle yeni kategori ekleyin.",
                "Kütüphanedeki bir sembolün adını, açıklamasını veya şeklini değiştirmek için sağ tıklayın ya da 'Düzenle'yi seçin.",
                "Açık projedeki sembole atama yapmak için Ekle > Sembol ekle veya Semboller panelindeki 'Seçili sembole ata'yı kullanın.",
            ],
            StepsEn:
            [
                "Open Insert > Product Library to search by manufacturer, order code, or category.",
                "Drag a product or symbol directly onto any blank schematic page (or click 'Place on Page') to insert it.",
                "Drag misclassified symbols onto any folder on the left to move them, or create a new category.",
                "Right-click or edit a library product/symbol to update its display name, description, pins, or symbol geometry.",
                "Use Insert > Change Symbol or 'Assign Symbol' to swap the symbol of an existing placed device.",
            ],
            Shortcuts: ["S", "R", "Ctrl+C", "Ctrl+V"],
            TipsTr:
            [
                "Çok bacaklı sürücülerde (ör. LXM62) iç blok yazıları (BUS BAR MODULE, A_DI1..B_DI6, SERCOS3) sembolle birlikte taşınır ve sayfaya eksiksiz yerleşir.",
                "Kendi oluşturduğunuz semboller varsayılan olarak 'Kendi sembollerim' ya da seçtiğiniz özel klasöre kaydedilir.",
            ],
            TipsEn:
            [
                "Complex drives keep all internal labels (such as BUS BAR MODULE, DI/DO and SERCOS3 tags) aligned with their pins when placed.",
                "User-edited copies of standard symbols save safely into your custom library folder.",
            ],
            Keywords: "kütüphane ürün sembol sürükle bırak kategori klasör taşı ekle düzenle product library drag drop category edit",
            BuildIllustration: BuildLibraryIllustration),

        new(
            Id: "blok-editoru",
            TitleTr: "4. Blok Editörü (Sembol & Yazı Düzenleme, Büyüyen Tuval)",
            TitleEn: "4. Block Editor (Label Editing & Dynamic Canvas)",
            SummaryTr: "Geniş semboller açıldığında beyaz çizim tuvali ve noktalı ızgara otomatik büyür; döndürülmüş veya sıkışık yazı etiketlerinin her biri tekil olarak seçilip düzenlenebilir.",
            SummaryEn: "The white drafting sheet expands automatically to fit large multi-pin drives; every straight or rotated (-45° / -90°) label is individually selectable and editable.",
            StepsTr:
            [
                "Semboller panelinden 'Sembolü düzenle…' veya şeritten Ekle > Blok editörü ile blok editörünü açın.",
                "Geniş bir sürücü (ör. 190 mm genişlikte LXM62) açıldığında arka beyaz fon ve ızgara nokta alanı tüm şekli ve uçları kapsayacak şekilde otomatik genişler.",
                "A_DI1, BUS BAR MODULE veya -90° döndürülmüş CN1 etiketlerinden herhangi birinin üstüne tıklayın; yalnızca o yazı objesi mavi seçim çerçevesiyle yakalanır.",
                "Sağ paneldeki 'Seçili yazı' kutularından Metin, Yükseklik (mm), Açı (°) ve Hiza alanlarını anında güncelleyin veya yazı üstüne çift tıklayın.",
                "Değişikliği kaydetmek için 'Kütüphaneye kaydet' ya da 'Projeye kaydet' düğmesini kullanın; hata olursa Ctrl+Z ile geri alın.",
            ],
            StepsEn:
            [
                "Click Insert > Block Editor or select a symbol and choose 'Edit Symbol…'.",
                "Wide devices automatically enlarge the white canvas and snap grid so pins and edges stay inside the sheet.",
                "Click directly on any label — even rotated -45° pin captions or -90° vertical module titles — to select that individual text element.",
                "Edit text, height (mm), rotation angle, and alignment in the 'Selected Text' panel or double-click the label on canvas.",
                "Save back to the active project or symbol library; Ctrl+Z undoes every geometry or label change.",
            ],
            Shortcuts: ["Ctrl+Z", "Ctrl+Y", "Delete", "Space"],
            TipsTr:
            [
                "Eğer iki çizgi veya dikdörtgen tam bir yazının altındaysa, Blok Editörü seçimi önce yazı ve bağlantı uçlarına öncelik verecek şekilde çalışır.",
                "'Yüzeye uç' komutuyla bir bloğun alt/üst kenarına otomatik aralıklarla seri uç ekleyebilirsiniz.",
            ],
            TipsEn:
            [
                "Selection prioritizes pins and text labels over large enclosing background rectangles.",
                "Use 'Surface Pins' to distribute evenly spaced pins along any edge of a block box.",
            ],
            Keywords: "blok editörü sembol label yazı döndürme büyüyen tuval beyaz fon seçili yazı block editor label rotated text canvas",
            BuildIllustration: BuildBlockEditorIllustration),

        new(
            Id: "baglanti-renk",
            TitleTr: "5. Bağlantılar, Tel Numaralandırma, Renk Kartelası ve Çizgi Biçimleri",
            TitleEn: "5. Wiring, Numbering, Color Palette & Line Styles",
            SummaryTr: "Bağlantılara renk kartelasından standart IEC/özel renk ve çizgi biçimi (Düz, Kesik çizgili, Çizgi-nokta, Noktalı) atayabilir; tüm telleri baştan numaralamadan önce onay alıp tek tıkla geri alabilirsiniz.",
            SummaryEn: "Assign standard IEC/custom wire colors and line dash styles via the color palette swatch grid, and confirm or undo full-project renumbering safely.",
            StepsTr:
            [
                "Bağlantı > Tel çek (W) ile iki pin arasında tel çizin veya sürükleyin.",
                "Yeni tellere numara vermek için Bağlantı > Yeni telleri numarala düğmesini kullanın; mevcut numaralara dokunulmaz.",
                "Tüm projeyi sıfırdan numaralamak için Bağlantı > Baştan numarala'ya tıklayın. Program yanlışlıkla basmaya karşı 'Emin misiniz?' onayı ister; onaylarsanız tüm işlem Ctrl+Z ile tek adımda geri alınabilir.",
                "Bağlantı > Kablo ve damar… penceresini açarak sağ alandaki 'Renk kartelası' kutularından ana/IEC renk seçin ve 'Çizgi biçimi' listesinden Düz, Kesik çizgili, Çizgi-nokta veya Noktalı stilini uygulayın.",
                "Bağlantı > Standart türleri ekle ile Güç, Kumanda, PE, Nötr, Bus ve Ekran türlerini projeye ekleyip şemada renk/kalınlık farkı elde edin.",
            ],
            StepsEn:
            [
                "Draw orthogonal wires between any two component pins with Wiring > Draw Wire (W).",
                "Number only unnumbered connections via Wiring > Number New Wires without altering existing wire IDs.",
                "Use Wiring > Renumber All when regenerating every wire number; a confirmation prompt prevents accidental clicks and Ctrl+Z reverts the entire batch.",
                "Open Wiring > Cables & Wires… to pick from the 13-color IEC/hex swatch palette and choose Solid, Dashed, Dash-dot, or Dotted line styles.",
                "Click Wiring > Add Standard Types to add Power, Control, PE, Neutral, and Bus connection styles.",
            ],
            Shortcuts: ["W", "Ctrl+Z", "Ctrl+Y", "Esc"],
            TipsTr:
            [
                "Kablo damarlarındaki renk metnine (örn. RD, BU, GNYE, BK, #E67E22) hem tel hem damar modunda tıklayarak renk kartelasını kullanabilirsiniz.",
                "Kesik çizgili veya Noktalı stiller şemada ve PDF/SVG çıktısında birebir aynı görünür.",
            ],
            TipsEn:
            [
                "All 13 IEC color codes (RD, BU, BK, BN, WH, GY, OG, YE, GN, VT, PK, GNYE, TQ) plus custom #RRGGBB hex colors work seamlessly.",
                "Selected line dash patterns render identically on screen, SVG, and vector PDF exports.",
            ],
            Keywords: "tel bağlantı baştan numarala emin misin geri al renk kartelası kesik çizgi noktalı kablo damar wire renumber color palette dashed solid",
            BuildIllustration: BuildWiringAndColorIllustration),

        new(
            Id: "plc-semalar",
            TitleTr: "6. PLC Adres Tablosu, Haberleşme ve Tek Hat Şemaları",
            TitleEn: "6. PLC I/O Table, Communication & Single-Line Diagrams",
            SummaryTr: "Tek tıkla projeye başlangıç PLC denetleyicisi (8 DI / 8 DO) ekleyebilir, Excel/CSV listesiyle adres atayabilir, ayrıca Haberleşme ve Tek Hat şeması sayfalarını otomatik üretebilirsiniz.",
            SummaryEn: "Create a starter PLC controller (8 DI / 8 DO) in one click, import CSV I/O address lists, and auto-generate Communication and Single-Line diagram pages.",
            StepsTr:
            [
                "Bağlantı > PLC adres / tel tablosu… penceresini açın; projede PLC yoksa sağ üstteki 'Yeni PLC ekle (8 DI / 8 DO)' düğmesiyle anında A1/PLC1 oluşturun.",
                "İsterseniz 'CSV adres yapıştır…' veya 'CSV dosyasından al…' ile elinizdeki I/O tablosunu topluca yükleyin (projede PLC yoksa otomatik oluşturulur).",
                "Tablodan bir satır seçip PLC adresi (%I0.0), sembolik ad (bAcilStop), açıklama ve bağlı olduğu saha cihazını güncelleyin.",
                "Projede Bus/Ethernet veya PLC ağ düğümleri varsa Bağlantı > Haberleşme şeması otomatik ağ sayfası üretir.",
                "Güç besleme zinciriniz çizildiğinde Bağlantı > Tek hat şeması blok özet sayfasını oluşturur (önkoşul eksikse bilgi penceresiyle nedenini bildirir).",
            ],
            StepsEn:
            [
                "Open Wiring > PLC Address / Wire Table…. If no controller exists yet, click 'Quick Add PLC (8 DI / 8 DO)' to generate one immediately.",
                "Paste or import CSV/semicolon I/O lists; an empty project automatically receives a matching PLC module during import.",
                "Edit PLC address (%I0.0), symbolic name, description, and field function binding on any row.",
                "Run Wiring > Communication Diagram to generate a network topology sheet from project bus nodes.",
                "Run Wiring > Single-Line Diagram to generate a power feeder overview page.",
            ],
            Shortcuts: [],
            TipsTr:
            [
                "PLC navigator paneline sağ tıklayarak PLC adres tablosuna hızlıca ulaşabilirsiniz.",
                "Haberleşme veya Tek Hat komutu projede yeterli ağ/güç bağlantısı bulamazsa nedenini açıkça gösteren uyarı penceresi açar.",
            ],
            TipsEn:
            [
                "Right-click inside the PLC dock panel to jump straight to the PLC cross-reference editor.",
                "If prerequisites are missing for Communication or Single-Line pages, an informative message explains what to connect first.",
            ],
            Keywords: "plc adres tablo csv haberleşme tek hat şeması communication single line controller I/O",
            BuildIllustration: BuildPlcAndDiagramsIllustration),

        new(
            Id: "antet-cikti",
            TitleTr: "7. Antet Tasarımı, Firma Logosu ve PDF/SVG/BOM Çıktısı",
            TitleEn: "7. Title Block Editor, Company Logo & PDF/SVG/BOM Export",
            SummaryTr: "Proje antetinin sütun ve metinlerini görsel olarak özelleştirebilir, firma logonuzu antet kutusu içine konumlandırabilir ve tek tıklamayla tüm proje raporlarını dışa aktarabilirsiniz.",
            SummaryEn: "Customize title block columns and fields visually, anchor your company logo inside the title block box, and export PDF packages, DXF sheets, BOMs, and terminal plans.",
            StepsTr:
            [
                "Sayfa > Antet oluştur / düzenle… penceresini açarak alt/üst çerçeve metinlerini ve logo alanını ayarlayın.",
                "'Logo seç…' düğmesi ile PNG/JPG logonuzu seçin; logo proje dosyasına göreli olarak bağlanır ve antetin logo bölmesinde orantılı çizilir.",
                "Dosya > Proje özellikleri… bölümünden Proje adı, Müşteri adı, Adres, Çizen, Kontrol eden ve Onaylayan alanlarını doldurun.",
                "Dosya > Revizyon ekle… ile her sayfaya revizyon kodu ve tarihi işleyin.",
                "Dosya > Dışa aktar / Raporlar… ile Çok Sayfalı Akıllı PDF (tıklanabilir çapraz referanslı), DXF/SVG, Malzeme Listesi (BOM) ve Klemens/Kablo planları alın.",
            ],
            StepsEn:
            [
                "Open Page > Edit Title Block… to adjust frame header/footer fields and title box columns.",
                "Attach your company logo (PNG/JPG); it scales proportionally inside the dedicated title block logo cell.",
                "Set Customer, Project Title, Designer, Checker, and Approver metadata in File > Project Properties….",
                "Record tracked page revisions via File > Add Revision….",
                "Use File > Export / Reports… to produce multi-page clickable PDFs, DXF/SVG sheets, Excel/CSV BOMs, and terminal schedules.",
            ],
            Shortcuts: ["Ctrl+E", "Ctrl+S"],
            TipsTr:
            [
                "Dışa aktarılan çok sayfalı PDF dosyasında kontak ve çapraz referans koordinatlarına tıklandığında ilgili hedef sayfaya otomatik atlanır.",
            ],
            TipsEn:
            [
                "Exported multi-page PDF files include clickable cross-reference links jumping between coil and contact locations.",
            ],
            Keywords: "antet logo resim dışa aktar pdf svg dxf bom malzeme revizyon proje özellikleri title block export",
            BuildIllustration: BuildTitleBlockAndExportIllustration),

        new(
            Id: "tanima-ai",
            TitleTr: "8. PDF/DXF Grafik Tanıma ve Yapay Zeka (Ollama / Claude / Antigravity)",
            TitleEn: "8. PDF/DXF Recognition & AI (Ollama / Claude / Antigravity)",
            SummaryTr: "PDF veya DXF çizimlerinden cihaz ve tel bağlantılarını otomatik tanıyabilir; Yerel Ollama, Claude (Anthropic), Antigravity (Gemini / Proxy) veya OpenAI uç noktalarıyla şemanıza doğal dilde değişiklik önerebilirsiniz.",
            SummaryEn: "Import vector PDF/DXF drawings, recognize devices and wires into electrical sheets, and edit schematics via Local Ollama, Claude (Anthropic), Antigravity (Gemini / Proxy), or OpenAI.",
            StepsTr:
            [
                "Dosya > PDF'ten al veya DXF/DWG al… ile dış çizimi bir grafik sayfasına yükleyin.",
                "Araçlar > Grafikten cihaz / bağlantı tanı… komutuyla ham çizgileri kütüphane sembollerine ve tellere dönüştürün.",
                "Araçlar > Yapay zeka ile değiştir penceresini açın ve Sağlayıcı kutusundan 'Yerel Ollama', 'Claude (Anthropic)', 'Antigravity (Gemini / Proxy)' veya 'OpenAI uyumlu uç nokta' seçin.",
                "Bulut sağlayıcılar için penceredeki maskeli 'API anahtarı' alanına anahtarınızı girin (anahtar yalnızca aktif oturumda bellekte tutulur, diske kaydedilmez) veya ortam değişkeni tanımlayın.",
                "İsteğinizi yazıp 'Öneri iste'ye basın; önerilen JSON yamalarını inceledikten sonra 'Uygula' ile tek adımda geri alınabilir şekilde projeye uygulayın.",
            ],
            StepsEn:
            [
                "Load external drawings via File > Import PDF or Import DXF/DWG….",
                "Run Tools > Recognize Devices / Wires… to turn vector geometry into live electrical devices and wires.",
                "Open Tools > Edit with AI and select Local Ollama, Claude (Anthropic), Antigravity (Gemini / Proxy), or OpenAI-compatible endpoint.",
                "For cloud providers, enter your API key in the masked session field (kept in memory only, never saved to disk) or set an environment variable.",
                "Write your request, click 'Ask AI', inspect the previewed changes, and click 'Apply' (fully undoable via Ctrl+Z).",
            ],
            Shortcuts: ["Ctrl+Z"],
            TipsTr:
            [
                "Güvenlik gereği API anahtarları yapay-zeka.json dosyasına ASLA yazılmaz; yalnızca uç nokta URL'si ve son model adı hatırlanır.",
                "İnternetsiz ortamda çalışıyorsanız Yerel Ollama (http://127.0.0.1:11434) ile veriniz cihazınızdan dışarı çıkmadan öneri alabilirsiniz.",
            ],
            TipsEn:
            [
                "API keys are never written to yapay-zeka.json; only provider, model name, and endpoint URL are saved.",
                "Local Ollama keeps all schematic data offline on your own workstation.",
            ],
            Keywords: "yapay zeka ai claude anthropic antigravity gemini ollama openai api anahtar pdf dxf tanı recognition",
            BuildIllustration: BuildAiAndRecognitionIllustration),

        new(
            Id: "kisayollar-dil",
            TitleTr: "9. Kısayollar, Türkçe / İngilizce Arayüz ve Hızlı Başvuru",
            TitleEn: "9. Shortcuts, Turkish / English UI & Quick Reference",
            SummaryTr: "Tek tıkla arayüzü Türkçe ↔ İngilizce arasında değiştirebilir, kısayolları Araçlar > Kısayollar altından özelleştirebilir ve bu yardım dosyasını istediğiniz zaman F1 ile açabilirsiniz.",
            SummaryEn: "Switch the entire application language between Turkish and English on the fly, customize hotkeys under Tools > Shortcuts, and open this guide anytime with F1.",
            StepsTr:
            [
                "Araçlar > Ayarlar > Dil: TR / EN düğmesine tıklayarak tüm şerit, yan paneller, durum çubuğu ve diyalogları Türkçe veya İngilizce yapın (tercih kaydedilir).",
                "Araçlar > Kısayollar ile tüm klavye kısayollarını görüntüleyip kendi alışkanlıklarınıza göre değiştirin.",
                "Hızlı erişim çubuğundaki '?' simgesine veya F1 tuşuna basarak görselli yardım kılavuzuna ve canlı aramaya ulaşın.",
                "İstediğinizde kılavuz penceresindeki 'HTML Yardım Dosyasını Kaydet / Aç…' düğmesiyle çevrimdışı tarayıcı kopyası (yardim.html) üretebilirsiniz.",
            ],
            StepsEn:
            [
                "Click Tools > Settings > Language: EN / TR to toggle the Ribbon, panels, context menus, status bar, and dialogs between Turkish and English.",
                "Customize keyboard bindings anytime under Tools > Shortcuts.",
                "Press F1 or click the '?' icon in the Quick Access toolbar to search this illustrated guide.",
                "Export a self-contained browser copy (yardim.html) from the Help window for offline viewing.",
            ],
            Shortcuts: ["F1", "W", "Space", "Ctrl+Z", "Ctrl+Y", "Delete", "Home", "G", "F11", "Ctrl+S"],
            TipsTr:
            [
                "Güncel kurulum paketi için klasördeki setup.bat dosyasına çift tıklamanız yeterlidir; masaüstü simgesi ve yardim.html otomatik güncellenir.",
            ],
            TipsEn:
            [
                "Double-click setup.bat in the distribution folder to refresh the desktop shortcut, executable, and yardim.html together.",
            ],
            Keywords: "kısayol dil ingilizce türkçe english turkish language shortcut f1 help yardım html setup",
            BuildIllustration: BuildShortcutsAndLanguageIllustration),
    ];

    public static IReadOnlyList<HelpTopic> Search(string? query, UiLanguage lang)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Topics;
        }

        var tokens = ComponentLibrary.FoldText(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Topics;
        }

        return [.. Topics.Where(topic =>
        {
            var corpus = topic.SearchCorpus(lang);
            return tokens.All(token => corpus.Contains(token, StringComparison.Ordinal));
        })];
    }

    /// <summary>Tüm konuları inline SVG diyagramları ve canlı JavaScript araması içeren tek bir HTML dosyasına yazar.</summary>
    public static void ExportStandaloneHtml(string path, UiLanguage lang)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var isEn = lang == UiLanguage.English;
        var pageTitle = isEn ? "aktapokus-electric — Illustrated User Guide" : "aktapokus-electric — Görselli Kullanım Kılavuzu";
        var tocHeading = isEn ? "Table of Contents" : "İçindekiler";
        var searchPlaceholder = isEn ? "Search topics, commands, shortcuts..." : "Konu, komut veya özellik ara…";
        var stepsHeading = isEn ? "Step-by-Step" : "Adım Adım Kullanım";
        var tipsHeading = isEn ? "Tips" : "İpuçları";
        var shortcutsHeading = isEn ? "Shortcuts" : "Kısayollar";
        var diagramCaption = isEn ? "Visual Diagram" : "Görsel Anlatım";
        var noMatchText = isEn ? "No matching help topics found." : "Aramanızla eşleşen yardım konusu bulunamadı.";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html lang=\"{(isEn ? "en" : "tr")}\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"  <title>{Enc(pageTitle)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#f4efe3; --panel:#ffffff; --ink:#1f2328; --muted:#5a6270; --accent:#1f6fd8; --line:#d8d1c2; --tip:#f0f6ff; }");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; font-family:'Segoe UI',Inter,Arial,sans-serif; background:var(--bg); color:var(--ink); display:flex; min-height:100vh; }");
        sb.AppendLine("    aside { width:320px; background:#faf7f0; border-right:1px solid var(--line); padding:20px 16px; position:sticky; top:0; height:100vh; overflow-y:auto; flex-shrink:0; }");
        sb.AppendLine("    aside h1 { font-size:18px; margin:0 0 12px 0; color:var(--ink); }");
        sb.AppendLine("    #search { width:100%; padding:10px 12px; border:1px solid var(--line); border-radius:8px; font-size:14px; background:#fff; margin-bottom:10px; }");
        sb.AppendLine("    #count { font-size:12px; color:var(--muted); margin-bottom:12px; }");
        sb.AppendLine("    nav a { display:block; padding:8px 10px; border-radius:6px; color:var(--ink); text-decoration:none; font-size:13.5px; line-height:1.35; margin-bottom:4px; }");
        sb.AppendLine("    nav a:hover, nav a.active { background:#e7effb; color:#114a9b; font-weight:600; }");
        sb.AppendLine("    main { flex:1; padding:28px 36px; max-width:980px; }");
        sb.AppendLine("    section { background:var(--panel); border:1px solid var(--line); border-radius:12px; padding:24px 26px; margin-bottom:24px; box-shadow:0 2px 6px rgba(0,0,0,.04); }");
        sb.AppendLine("    section h2 { margin:0 0 8px 0; font-size:20px; color:#12386e; }");
        sb.AppendLine("    .summary { font-size:14.5px; color:#313742; line-height:1.55; margin-bottom:16px; }");
        sb.AppendLine("    .diagram-box { border:1px solid var(--line); border-radius:8px; padding:12px; background:#fff; margin-bottom:16px; }");
        sb.AppendLine("    .diagram-label { font-size:12px; font-weight:600; color:var(--muted); margin-bottom:6px; text-transform:uppercase; letter-spacing:.03em; }");
        sb.AppendLine("    .diagram-box svg { width:100%; height:auto; display:block; }");
        sb.AppendLine("    h3 { font-size:14.5px; margin:14px 0 8px 0; color:var(--ink); }");
        sb.AppendLine("    ol { margin:0; padding-left:20px; line-height:1.6; font-size:14px; }");
        sb.AppendLine("    li { margin-bottom:6px; }");
        sb.AppendLine("    .keys { display:flex; flex-wrap:wrap; gap:8px; margin-top:8px; }");
        sb.AppendLine("    kbd { background:#eef1f6; border:1px solid #cbd2df; border-radius:5px; padding:3px 8px; font-size:12.5px; font-family:Consolas,monospace; }");
        sb.AppendLine("    .tips { background:var(--tip); border-left:4px solid var(--accent); padding:10px 14px; border-radius:6px; margin-top:14px; font-size:13.5px; line-height:1.5; }");
        sb.AppendLine("    .hidden { display:none !important; }");
        sb.AppendLine("    #empty { padding:24px; text-align:center; background:#fff; border:1px dashed var(--line); border-radius:10px; color:var(--muted); }");
        sb.AppendLine("    @media (max-width:860px) { body{flex-direction:column;} aside{width:100%;height:auto;position:static;} main{padding:18px;} }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <aside>");
        sb.AppendLine($"    <h1>{Enc(pageTitle)}</h1>");
        sb.AppendLine($"    <input id=\"search\" type=\"search\" placeholder=\"{Enc(searchPlaceholder)}\" autocomplete=\"off\">");
        sb.AppendLine($"    <div id=\"count\">{Topics.Count} / {Topics.Count}</div>");
        sb.AppendLine($"    <div class=\"diagram-label\">{Enc(tocHeading)}</div>");
        sb.AppendLine("    <nav id=\"toc\">");
        foreach (var topic in Topics)
        {
            sb.AppendLine($"      <a href=\"#{Enc(topic.Id)}\" data-id=\"{Enc(topic.Id)}\">{Enc(topic.TitleFor(lang))}</a>");
        }
        sb.AppendLine("    </nav>");
        sb.AppendLine("  </aside>");
        sb.AppendLine("  <main>");
        foreach (var topic in Topics)
        {
            var svg = SvgExporter.Write(topic.BuildIllustration(lang));
            var corpus = topic.SearchCorpus(lang);
            sb.AppendLine($"    <section id=\"{Enc(topic.Id)}\" data-search=\"{Enc(corpus)}\">");
            sb.AppendLine($"      <h2>{Enc(topic.TitleFor(lang))}</h2>");
            sb.AppendLine($"      <p class=\"summary\">{Enc(topic.SummaryFor(lang))}</p>");
            sb.AppendLine("      <div class=\"diagram-box\">");
            sb.AppendLine($"        <div class=\"diagram-label\">{Enc(diagramCaption)}</div>");
            sb.AppendLine(svg);
            sb.AppendLine("      </div>");
            sb.AppendLine($"      <h3>{Enc(stepsHeading)}</h3>");
            sb.AppendLine("      <ol>");
            foreach (var step in topic.StepsFor(lang))
            {
                sb.AppendLine($"        <li>{Enc(step)}</li>");
            }
            sb.AppendLine("      </ol>");
            if (topic.Shortcuts.Count > 0)
            {
                sb.AppendLine($"      <h3>{Enc(shortcutsHeading)}</h3>");
                sb.AppendLine("      <div class=\"keys\">");
                foreach (var key in topic.Shortcuts)
                {
                    sb.AppendLine($"        <kbd>{Enc(key)}</kbd>");
                }
                sb.AppendLine("      </div>");
            }

            if (topic.TipsFor(lang).Count > 0)
            {
                sb.AppendLine($"      <div class=\"tips\"><strong>{Enc(tipsHeading)}:</strong><ul>");
                foreach (var tip in topic.TipsFor(lang))
                {
                    sb.AppendLine($"        <li>{Enc(tip)}</li>");
                }
                sb.AppendLine("      </ul></div>");
            }

            sb.AppendLine("    </section>");
        }

        sb.AppendLine($"    <div id=\"empty\" class=\"hidden\">{Enc(noMatchText)}</div>");
        sb.AppendLine("  </main>");
        sb.AppendLine("  <script>");
        sb.AppendLine("    function foldText(s){return (s||'').toLowerCase().replace(/ı/g,'i').replace(/ğ/g,'g').replace(/ü/g,'u').replace(/ş/g,'s').replace(/ö/g,'o').replace(/ç/g,'c').normalize('NFD').replace(/[\\u0300-\\u036f]/g,'');}");
        sb.AppendLine("    const input=document.getElementById('search'), sections=[...document.querySelectorAll('main section')], links=[...document.querySelectorAll('#toc a')], count=document.getElementById('count'), empty=document.getElementById('empty');");
        sb.AppendLine("    input.addEventListener('input',()=>{const tokens=foldText(input.value).trim().split(/\\s+/).filter(Boolean); let visible=0;");
        sb.AppendLine("      sections.forEach((sec,i)=>{const hay=sec.getAttribute('data-search')||''; const show=tokens.every(t=>hay.includes(t)); sec.classList.toggle('hidden',!show); links[i].classList.toggle('hidden',!show); if(show)visible++;});");
        sb.AppendLine($"      count.textContent=visible+' / {Topics.Count}'; empty.classList.toggle('hidden',visible>0);");
        sb.AppendLine("    });");
        sb.AppendLine("  </script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Enc(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    private static Scene BuildWorkspaceIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.35),
            // Ribbon
            new RectItem(6, 6, 148, 16, 0.3),
            new LineItem(6, 11, 154, 11, 0.2, "#1F6FD8"),
            new TextItem(12, 10, en ? "File | Home | Insert | Wiring | Page | View | Tools" : "Dosya | Başlangıç | Ekle | Bağlantı | Sayfa | Görünüm | Araçlar", 2.8, Font, Color: "#1F6FD8"),
            new TextItem(80, 18.5, en ? "Command Ribbon & Quick Access" : "Komut Şeridi ve Hızlı Erişim", 2.8, Font, TextAlign.Center),
            // Pages left panel
            new RectItem(6, 24, 32, 52, 0.3),
            new TextItem(22, 29, en ? "Pages" : "Sayfalar", 3.0, Font, TextAlign.Center, Color: "#12386E"),
            new LineItem(8, 32, 36, 32, 0.2),
            new TextItem(10, 37, "1  0001 Kapak", 2.5, Font),
            new TextItem(10, 42, "2  0002 Giriş", 2.5, Font, Color: "#1F6FD8"),
            new TextItem(10, 47, "3  0003 Sürücü", 2.5, Font),
            new RectItem(8, 53, 28, 20, 0.2),
            new TextItem(22, 64, en ? "Preview" : "Önizleme", 2.5, Font, TextAlign.Center, Color: "#5A6270"),
            // Central Canvas
            new RectItem(40, 24, 80, 52, 0.35),
            new TextItem(80, 30, en ? "Main Schematic Canvas (Tabs)" : "Sekmeli Ana Çizim Tuvali", 3.2, Font, TextAlign.Center, Color: "#12386E"),
            new RectItem(56, 38, 16, 18, 0.3),
            new TextItem(64, 48, "-K1", 3.0, Font, TextAlign.Center),
            new LineItem(64, 34, 64, 38, 0.35, "#C0392B"),
            new LineItem(64, 56, 64, 62, 0.35, "#1F6FD8"),
            new RectItem(88, 38, 18, 18, 0.3),
            new TextItem(97, 48, "-Q1", 3.0, Font, TextAlign.Center),
            new LineItem(72, 47, 88, 47, 0.35, "#2E8B57", LineStyle.Dashed),
            // Right dock
            new RectItem(122, 24, 32, 52, 0.3),
            new TextItem(138, 29, en ? "Symbols / Devices" : "Sembol / Cihaz", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new LineItem(124, 32, 152, 32, 0.2),
            new RectItem(126, 36, 24, 14, 0.2),
            new TextItem(138, 44, "K / Q / M / PLC", 2.5, Font, TextAlign.Center),
            new TextItem(138, 60, en ? "Drag & Drop" : "Sürükle-Bırak", 2.6, Font, TextAlign.Center, Color: "#1F6FD8"),
            // Status bar
            new RectItem(6, 78, 148, 6, 0.25),
            new TextItem(10, 82, en ? "Status & Info" : "Durum & Bildirim", 2.4, Font),
            new TextItem(142, 82, en ? "ERC clean" : "ERC temiz", 2.4, Font, TextAlign.Right, Color: "#2E8B57"),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildPageManagementIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            // Tree mode box
            new RectItem(8, 8, 66, 74, 0.3),
            new TextItem(41, 14, en ? "Tree View + Subpage (3.1)" : "Ağaç Görünümü + Ara Sayfa (3.1)", 3.0, Font, TextAlign.Center, Color: "#12386E"),
            new TextItem(14, 24, "+ =M1  0001  Kapak", 2.8, Font),
            new TextItem(14, 32, "   2  0002  Besleme", 2.8, Font),
            new TextItem(14, 40, "   3  0003  Sürücü", 2.8, Font),
            new RectItem(16, 44, 52, 8, 0.25),
            new TextItem(19, 49.5, "   3.1  0003a  Ek", 2.8, Font, Color: "#1F6FD8"),
            new TextItem(14, 60, "   4  0004  Klemens", 2.8, Font),
            // Drag-drop box
            new RectItem(84, 8, 68, 74, 0.3),
            new TextItem(118, 14, en ? "Drag & Drop Reorder" : "Sürükle-Bırak Sıra Değiştir", 3.0, Font, TextAlign.Center, Color: "#12386E"),
            new RectItem(92, 24, 52, 11, 0.25),
            new TextItem(96, 31, en ? "Page 4  (Dragged)" : "Sayfa 4  (Sürüklenen)", 2.7, Font, Color: "#C0392B"),
            new LineItem(118, 35, 118, 49, 0.4, "#1F6FD8", LineStyle.Dashed),
            new CircleItem(118, 49, 1.3, 0.3, Filled: true),
            new RectItem(92, 50, 52, 11, 0.25),
            new TextItem(96, 57, en ? "Target: Page 2" : "Hedef: Sayfa 2", 2.7, Font, Color: "#1F6FD8"),
            new TextItem(118, 72, en ? "Asks ConfirmDialog first" : "Taşımadan önce onay sorar", 2.7, Font, TextAlign.Center, Color: "#2E8B57"),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildLibraryIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            // Left Categories
            new RectItem(8, 8, 44, 74, 0.3),
            new TextItem(30, 14, en ? "Categories (+ New)" : "Kategoriler (+ Yeni)", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new TextItem(12, 24, "Kontaktör / Röle", 2.6, Font),
            new TextItem(12, 32, "Motor ve Sürücü", 2.6, Font),
            new RectItem(10, 36, 40, 8, 0.2),
            new TextItem(12, 41.5, en ? "Otomasyon (Drop)" : "Otomasyon (Bırak)", 2.6, Font, Color: "#1F6FD8"),
            new TextItem(12, 51, "PLC ve G/Ç", 2.6, Font),
            // Middle symbols & move arrow
            new RectItem(56, 8, 46, 74, 0.3),
            new TextItem(79, 14, en ? "Products / Symbols" : "Ürünler / Semboller", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new RectItem(60, 22, 38, 12, 0.25),
            new TextItem(79, 29, "LXM62PD84A11000", 2.4, Font, TextAlign.Center),
            new LineItem(60, 28, 48, 40, 0.35, "#1F6FD8", LineStyle.Dashed),
            new RectItem(60, 46, 38, 22, 0.25),
            new TextItem(79, 54, en ? "Edit Name / Icon" : "Ad / Şekil Düzenle", 2.5, Font, TextAlign.Center, Color: "#2E8B57"),
            new TextItem(79, 63, "CN1..CN8 (26 pin)", 2.3, Font, TextAlign.Center),
            // Right Target Page
            new RectItem(108, 8, 44, 74, 0.35),
            new TextItem(130, 14, en ? "Blank Page" : "Boş Şema Sayfası", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new LineItem(98, 28, 118, 42, 0.45, "#2E8B57"),
            new CircleItem(118, 42, 1.3, 0.3, Filled: true),
            new RectItem(116, 40, 28, 24, 0.3),
            new TextItem(130, 53, "LXM62", 3.0, Font, TextAlign.Center),
            new TextItem(130, 74, en ? "Drag to Page" : "Sayfaya Sürükle", 2.5, Font, TextAlign.Center, Color: "#2E8B57"),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildBlockEditorIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            // White dynamic canvas
            new RectItem(8, 8, 96, 74, 0.35),
            new TextItem(56, 14, en ? "Auto-Expanding White Canvas" : "Otomatik Büyüyen Beyaz Tuval", 2.8, Font, TextAlign.Center, Color: "#1F6FD8"),
            // Wide drive symbol inside
            new RectItem(14, 24, 84, 38, 0.35),
            new LineItem(32, 24, 32, 62, 0.25),
            new LineItem(66, 24, 66, 62, 0.25),
            new LineItem(14, 44, 98, 44, 0.25),
            // Pins
            new LineItem(20, 20, 20, 24, 0.3),
            new CircleItem(20, 20, 1.0, 0.25),
            new LineItem(42, 20, 42, 24, 0.3),
            new CircleItem(42, 20, 1.0, 0.25),
            new LineItem(52, 20, 52, 24, 0.3),
            new CircleItem(52, 20, 1.0, 0.25),
            // Selected label highlight
            new RectItem(34, 47, 30, 10, 0.4),
            new TextItem(49, 53.5, "DIGITAL I/O", 2.6, Font, TextAlign.Center, Color: "#1F6FD8"),
            new TextItem(23, 35, "BUS BAR", 2.6, Font, TextAlign.Center),
            new TextItem(43, 32, "A_DI1", 2.3, Font),
            new TextItem(53, 32, "A_DI2", 2.3, Font),
            new TextItem(82, 35, "SERCOS3", 2.5, Font, TextAlign.Center),
            new TextItem(56, 72, en ? "Every label is clickable" : "Her yazı tek tek seçilebilir", 2.6, Font, TextAlign.Center, Color: "#2E8B57"),
            // Right inspector
            new RectItem(108, 8, 44, 74, 0.3),
            new TextItem(130, 15, en ? "Selected Text" : "Seçili yazı", 3.0, Font, TextAlign.Center, Color: "#12386E"),
            new TextItem(112, 26, en ? "Text:" : "Metin:", 2.5, Font),
            new RectItem(112, 28, 36, 8, 0.25),
            new TextItem(114, 33.5, "DIGITAL I/O", 2.4, Font, Color: "#1F6FD8"),
            new TextItem(112, 44, en ? "Height: 2.5 mm" : "Yükseklik: 2.5", 2.4, Font),
            new TextItem(112, 53, en ? "Angle: -45° / 0°" : "Açı: -45° / 0°", 2.4, Font),
            new RectItem(112, 62, 36, 9, 0.25),
            new TextItem(130, 68, en ? "Apply Label" : "Yazıyı Uygula", 2.6, Font, TextAlign.Center, Color: "#2E8B57"),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildWiringAndColorIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            // Devices left & right with 4 differently styled/colored wires
            new RectItem(10, 12, 22, 66, 0.3),
            new TextItem(21, 18, "-Q1", 2.8, Font, TextAlign.Center),
            new RectItem(76, 12, 22, 66, 0.3),
            new TextItem(87, 18, "-K1", 2.8, Font, TextAlign.Center),
            // Wire 1: Red Solid
            new LineItem(32, 26, 76, 26, 0.55, "#C0392B", LineStyle.Solid),
            new TextItem(54, 24, en ? "101 · Solid (RD)" : "101 · Düz (RD)", 2.5, Font, TextAlign.Center, Color: "#C0392B"),
            // Wire 2: Blue DashDot
            new LineItem(32, 40, 76, 40, 0.45, "#1F6FD8", LineStyle.DashDot),
            new TextItem(54, 38, en ? "102 · Dash-Dot (BU)" : "102 · Çizgi-nokta (BU)", 2.5, Font, TextAlign.Center, Color: "#1F6FD8"),
            // Wire 3: Green Dashed
            new LineItem(32, 54, 76, 54, 0.45, "#2E8B57", LineStyle.Dashed),
            new TextItem(54, 52, en ? "PE · Dashed (GNYE)" : "PE · Kesik çizgi (GNYE)", 2.5, Font, TextAlign.Center, Color: "#2E8B57"),
            // Wire 4: Gray Dotted
            new LineItem(32, 68, 76, 68, 0.45, "#6E7681", LineStyle.Dotted),
            new TextItem(54, 66, en ? "104 · Dotted (GY)" : "104 · Noktalı (GY)", 2.5, Font, TextAlign.Center, Color: "#6E7681"),
            // Color palette & Confirm box on right
            new RectItem(104, 12, 48, 34, 0.3),
            new TextItem(128, 18, en ? "Color Palette" : "Renk Kartelası", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new LineItem(110, 26, 120, 26, 2.5, "#C0392B"),
            new LineItem(122, 26, 132, 26, 2.5, "#1F6FD8"),
            new LineItem(134, 26, 144, 26, 2.5, "#2E8B57"),
            new LineItem(110, 35, 120, 35, 2.5, "#1C1E21"),
            new LineItem(122, 35, 132, 35, 2.5, "#D97706"),
            new LineItem(134, 35, 144, 35, 2.5, "#7C3AED"),
            // Confirm prompt card
            new RectItem(104, 50, 48, 28, 0.3),
            new TextItem(128, 57, en ? "Renumber All?" : "Baştan Numarala?", 2.7, Font, TextAlign.Center, Color: "#C0392B"),
            new TextItem(128, 65, en ? "Confirm + Ctrl+Z" : "Emin misin? + Ctrl+Z", 2.4, Font, TextAlign.Center),
            new TextItem(128, 73, en ? "Undoable anytime" : "Tek adımda geri alınır", 2.2, Font, TextAlign.Center, Color: "#2E8B57"),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildPlcAndDiagramsIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            // PLC I/O box
            new RectItem(8, 8, 66, 74, 0.3),
            new TextItem(41, 15, en ? "PLC I/O Table (+ Quick Add)" : "PLC Tablosu (+ Yeni PLC)", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new LineItem(12, 19, 70, 19, 0.25),
            new TextItem(14, 26, "%I0.0 | bStart   | -S1", 2.4, Font),
            new TextItem(14, 34, "%I0.1 | bStop    | -S0", 2.4, Font),
            new TextItem(14, 42, "%Q0.0 | qMotor   | -K1", 2.4, Font),
            new RectItem(14, 52, 54, 10, 0.25),
            new TextItem(41, 58.5, en ? "+ Quick Add (8 DI / 8 DO)" : "+ Yeni PLC Ekle (8 DI / 8 DO)", 2.4, Font, TextAlign.Center, Color: "#1F6FD8"),
            new TextItem(41, 72, en ? "CSV / Excel Paste & Import" : "CSV Dosyası veya Yapıştır", 2.4, Font, TextAlign.Center, Color: "#2E8B57"),
            // Communication & Single line
            new RectItem(80, 8, 72, 35, 0.3),
            new TextItem(116, 15, en ? "Communication Diagram" : "Haberleşme Şeması", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new RectItem(88, 22, 18, 12, 0.25),
            new TextItem(97, 29, "PLC", 2.6, Font, TextAlign.Center),
            new RectItem(126, 22, 18, 12, 0.25),
            new TextItem(135, 29, "DRV", 2.6, Font, TextAlign.Center),
            new LineItem(106, 28, 126, 28, 0.45, "#1F6FD8"),
            new RectItem(80, 47, 72, 35, 0.3),
            new TextItem(116, 54, en ? "Single-Line Diagram" : "Tek Hat Şeması", 2.8, Font, TextAlign.Center, Color: "#12386E"),
            new LineItem(90, 66, 142, 66, 0.45, "#C0392B"),
            new RectItem(98, 62, 12, 8, 0.25),
            new RectItem(122, 62, 12, 8, 0.25),
            new TextItem(104, 67.5, "Q1", 2.4, Font, TextAlign.Center),
            new TextItem(128, 67.5, "M1", 2.4, Font, TextAlign.Center),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildTitleBlockAndExportIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            // Sheet frame + title block bottom strip
            new RectItem(10, 10, 100, 70, 0.35),
            new TextItem(60, 26, en ? "Drawing Sheet Frame" : "Sayfa Çerçevesi", 3.0, Font, TextAlign.Center, Color: "#5A6270"),
            new RectItem(10, 56, 100, 24, 0.35),
            new RectItem(12, 58, 24, 20, 0.3),
            new TextItem(24, 69, en ? "LOGO" : "FİRMA", 3.0, Font, TextAlign.Center, Color: "#1F6FD8"),
            new LineItem(38, 56, 38, 80, 0.25),
            new LineItem(74, 56, 74, 80, 0.25),
            new LineItem(38, 68, 110, 68, 0.25),
            new TextItem(56, 63, en ? "Project / Sheet" : "Proje / Sayfa", 2.5, Font, TextAlign.Center),
            new TextItem(56, 75, en ? "Drawn / Checked" : "Çizen / Kontrol", 2.5, Font, TextAlign.Center),
            new TextItem(92, 63, "Rev A", 2.6, Font, TextAlign.Center, Color: "#2E8B57"),
            new TextItem(92, 75, "1 / 12", 2.6, Font, TextAlign.Center),
            // Export list
            new RectItem(116, 10, 36, 70, 0.3),
            new TextItem(134, 17, en ? "Exports" : "Çıktılar", 2.9, Font, TextAlign.Center, Color: "#12386E"),
            new TextItem(120, 28, "• Smart PDF", 2.5, Font, Color: "#C0392B"),
            new TextItem(120, 38, "• DXF / SVG", 2.5, Font, Color: "#1F6FD8"),
            new TextItem(120, 48, "• BOM Excel", 2.5, Font, Color: "#2E8B57"),
            new TextItem(120, 58, "• Terminal", 2.5, Font),
            new TextItem(120, 68, "• Wire List", 2.5, Font),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildAiAndRecognitionIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            // PDF/DXF box
            new RectItem(8, 12, 34, 66, 0.3),
            new TextItem(25, 20, "PDF / DXF", 3.0, Font, TextAlign.Center, Color: "#12386E"),
            new TextItem(25, 40, en ? "Recognize" : "Grafikten", 2.5, Font, TextAlign.Center),
            new TextItem(25, 46, en ? "Vectors" : "Cihaz Tanı", 2.5, Font, TextAlign.Center),
            new LineItem(42, 45, 52, 45, 0.4, "#1F6FD8"),
            // AI hub box
            new RectItem(52, 12, 56, 66, 0.35),
            new TextItem(80, 19, en ? "AI Providers" : "Yapay Zeka", 3.0, Font, TextAlign.Center, Color: "#1F6FD8"),
            new RectItem(56, 24, 48, 9, 0.2),
            new TextItem(80, 30, "Yerel Ollama (Local)", 2.3, Font, TextAlign.Center),
            new RectItem(56, 36, 48, 9, 0.2),
            new TextItem(80, 42, "Claude (Anthropic)", 2.3, Font, TextAlign.Center, Color: "#C0392B"),
            new RectItem(56, 48, 48, 9, 0.2),
            new TextItem(80, 54, "Antigravity (Gemini)", 2.3, Font, TextAlign.Center, Color: "#2E8B57"),
            new RectItem(56, 60, 48, 9, 0.2),
            new TextItem(80, 66, "OpenAI Compatible", 2.3, Font, TextAlign.Center),
            // Undoable Patch Preview
            new LineItem(108, 45, 116, 45, 0.4, "#2E8B57"),
            new RectItem(116, 12, 36, 66, 0.3),
            new TextItem(134, 20, en ? "JSON Patch" : "Yama Önizleme", 2.7, Font, TextAlign.Center, Color: "#12386E"),
            new TextItem(134, 36, "+ Add K7", 2.5, Font, TextAlign.Center, Color: "#2E8B57"),
            new TextItem(134, 43, "+ Wire A2", 2.5, Font, TextAlign.Center, Color: "#2E8B57"),
            new TextItem(134, 50, "Ctrl+Z Safe", 2.5, Font, TextAlign.Center, Color: "#2E8B57"),
        };
        return new Scene(160, 90, items);
    }

    private static Scene BuildShortcutsAndLanguageIllustration(UiLanguage lang)
    {
        var en = lang == UiLanguage.English;
        var items = new List<SceneItem>
        {
            new RectItem(4, 4, 152, 82, 0.3),
            new TextItem(80, 14, en ? "Essential Shortcuts & Bilingual Toggle" : "Temel Kısayollar ve Türkçe / İngilizce Geçişi", 3.1, Font, TextAlign.Center, Color: "#12386E"),
            // Shortcut tiles
            new RectItem(10, 22, 32, 22, 0.25),
            new TextItem(26, 31, "F1", 3.4, Font, TextAlign.Center, Color: "#1F6FD8"),
            new TextItem(26, 39, en ? "Help" : "Yardım", 2.4, Font, TextAlign.Center),
            new RectItem(46, 22, 32, 22, 0.25),
            new TextItem(62, 31, "W", 3.4, Font, TextAlign.Center, Color: "#C0392B"),
            new TextItem(62, 39, en ? "Draw Wire" : "Tel Çek", 2.4, Font, TextAlign.Center),
            new RectItem(82, 22, 32, 22, 0.25),
            new TextItem(98, 31, "Ctrl+Z", 3.1, Font, TextAlign.Center, Color: "#2E8B57"),
            new TextItem(98, 39, en ? "Undo" : "Geri Al", 2.4, Font, TextAlign.Center),
            new RectItem(118, 22, 32, 22, 0.25),
            new TextItem(134, 31, "TR / EN", 3.0, Font, TextAlign.Center, Color: "#7C3AED"),
            new TextItem(134, 39, en ? "Language" : "Dil Seç", 2.4, Font, TextAlign.Center),
            // Second row
            new RectItem(10, 50, 44, 24, 0.25),
            new TextItem(32, 60, "Home / F11", 2.8, Font, TextAlign.Center),
            new TextItem(32, 68, en ? "Fit / Full" : "Sığdır / Tam", 2.3, Font, TextAlign.Center),
            new RectItem(58, 50, 44, 24, 0.25),
            new TextItem(80, 60, "Space / R", 2.8, Font, TextAlign.Center),
            new TextItem(80, 68, en ? "Rotate" : "Döndür", 2.3, Font, TextAlign.Center),
            new RectItem(106, 50, 44, 24, 0.25),
            new TextItem(128, 60, "setup.bat", 2.8, Font, TextAlign.Center, Color: "#1F6FD8"),
            new TextItem(128, 68, en ? "Update App" : "Kur & Güncelle", 2.3, Font, TextAlign.Center),
        };
        return new Scene(160, 90, items);
    }
}
