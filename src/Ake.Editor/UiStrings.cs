using System.Text;
using System.Text.Json;

namespace Ake.Editor;

/// <summary>Editör arayüzünün görüntüleme dili.</summary>
public enum UiLanguage
{
    Turkish,
    English,
}

/// <summary>
/// Editör arayüzü için Türkçe / İngilizce metin sözlüğü ve dil tercihi kalıcılığı.
/// Varsayılan dil Türkçedir; <see cref="Current"/> değiştirildiğinde şerit, menüler, paneller ve açılan pencereler seçili dili kullanır.
/// </summary>
public static class UiStrings
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // Dosya ve şerit sekmeleri / grupları
        ["Dosya"] = "File",
        ["Başlangıç"] = "Home",
        ["Ekle"] = "Insert",
        ["Bağlantı"] = "Wiring",
        ["Sayfa"] = "Page",
        ["Görünüm"] = "View",
        ["Araçlar"] = "Tools",
        ["Proje"] = "Project",
        ["Pano"] = "Clipboard",
        ["Düzen"] = "Edit",
        ["Seçim"] = "Selection",
        ["Sembol"] = "Symbol",
        ["Makro"] = "Macro",
        ["Klemens"] = "Terminal",
        ["Çizim"] = "Drawing",
        ["Numaralandırma"] = "Numbering",
        ["Bağlantı türleri"] = "Connection Types",
        ["Şemalar"] = "Diagrams",
        ["Gezinme"] = "Navigation",
        ["Antet / Şablon"] = "Title Block / Template",
        ["Sıra"] = "Order",
        ["Yakınlaştırma"] = "Zoom",
        ["Gösterim"] = "Display",
        ["Paneller"] = "Panels",
        ["Tag'ler"] = "Device Tags",
        ["Tanıma"] = "Recognition",
        ["Yapay zeka"] = "AI Assistant",
        ["Ayarlar"] = "Settings",
        ["Yardım"] = "Help",

        // Ana komutlar (EditorCommands)
        ["Yeni"] = "New",
        ["Aç"] = "Open",
        ["Kaydet"] = "Save",
        ["Farklı kaydet"] = "Save As",
        ["Projeyi kapat"] = "Close Project",
        ["Dışa aktar / Raporlar…"] = "Export / Reports…",
        ["PDF'ten al"] = "Import PDF",
        ["DXF/DWG al…"] = "Import DXF/DWG…",
        ["Liste al (Excel/CSV)…"] = "Import List (Excel/CSV)…",
        ["Proje özellikleri…"] = "Project Properties…",
        ["Revizyon ekle…"] = "Add Revision…",
        ["Çıkış"] = "Exit",
        ["Geri al"] = "Undo",
        ["Yinele"] = "Redo",
        ["Döndür"] = "Rotate",
        ["Özellikler"] = "Properties",
        ["Seçimi bırak"] = "Deselect",
        ["Sil"] = "Delete",
        ["Tümünü seç"] = "Select All",
        ["Kopyala"] = "Copy",
        ["Kes"] = "Cut",
        ["Yapıştır"] = "Paste",
        ["Tel çek"] = "Draw Wire",
        ["Sembol ekle…"] = "Insert Symbol…",
        ["Sembol değiştir…"] = "Change Symbol…",
        ["Metin ekle…"] = "Add Text…",
        ["Blok editörü"] = "Block Editor",
        ["Sembolü düzenle"] = "Edit Symbol",
        ["Kütüphane aç"] = "Open Library",
        ["Ürün kütüphanesi…"] = "Product Library…",
        ["Makro ekle"] = "Insert Macro",
        ["Klemens şeridi"] = "Terminal Strip",
        ["Sayfadan makro"] = "Macro from Page",
        ["Seçimden makro"] = "Macro from Selection",
        ["Baştan numarala"] = "Renumber All",
        ["Yeni telleri numarala"] = "Number New Wires",
        ["Numaralandırma ayarları…"] = "Numbering Settings…",
        ["Standart türleri ekle"] = "Add Standard Types",
        ["Kablo ve damar…"] = "Cables & Wires…",
        ["Potansiyel işareti…"] = "Potential Marker…",
        ["Haberleşme şeması sayfası"] = "Communication Diagram",
        ["Tek hat şeması sayfası"] = "Single-Line Diagram",
        ["PLC adres / tel tablosu…"] = "PLC Address / Wire Table…",
        ["Önceki sayfa"] = "Previous Page",
        ["Sonraki sayfa"] = "Next Page",
        ["Yeni sayfa…"] = "New Page…",
        ["Araya sayfa ekle (3.1)"] = "Insert Subpage (3.1)",
        ["Sayfa özellikleri…"] = "Page Properties…",
        ["Antet oluştur / düzenle…"] = "Edit Title Block…",
        ["Sayfa numaralarını baştan ver"] = "Renumber Pages",
        ["Sayfayı yukarı taşı"] = "Move Page Up",
        ["Sayfayı aşağı taşı"] = "Move Page Down",
        ["Sayfayı en başa taşı"] = "Move Page First",
        ["Sayfayı sığdır"] = "Fit to View",
        ["Izgara"] = "Grid",
        ["Koyu zemin"] = "Dark Canvas",
        ["Tel uçları"] = "Show Pins",
        ["Navigator"] = "Navigators",
        ["Mesajlar"] = "Messages",
        ["Şeridi daralt"] = "Collapse Ribbon",
        ["Tam çizim alanı"] = "Full Canvas",
        ["Varsayılan düzen"] = "Reset Layout",
        ["Tag'leri numarala"] = "Renumber Tags",
        ["Harf + sıra (K1)"] = "Letter + Index (K1)",
        ["Sayfa + harf + sıra (3K1)"] = "Page + Letter + Index (3K1)",
        ["Kısayollar"] = "Shortcuts",
        ["Dil: TR / EN"] = "Language: EN / TR",
        ["Yapay zeka ile değiştir"] = "Edit with AI",
        ["Grafikten cihaz / bağlantı tanı…"] = "Recognize Devices / Wires…",
        ["Hakkında"] = "About",
        ["Kullanım kılavuzu (Yardım)"] = "User Guide (Help)",

        // Blok editörü komutları
        ["Araç"] = "Tools",
        ["Yüzeye uç"] = "Surface Pins",
        ["Yeni ucun yönü"] = "New Pin Direction",
        ["Seç"] = "Select",
        ["Çizgi"] = "Line",
        ["Dikdörtgen"] = "Rectangle",
        ["Daire"] = "Circle",
        ["Yazı"] = "Text",
        ["Uç"] = "Pin",
        ["Ucu çevir"] = "Rotate Pin",
        ["İlk uç yap"] = "Make First Pin",
        ["Yukarı"] = "Up",
        ["Aşağı"] = "Down",
        ["Sola"] = "Left",
        ["Sağa"] = "Right",
        ["Projeye kaydet"] = "Save to Project",
        ["Dosyaya kaydet…"] = "Save to File…",
        ["Kütüphaneye kaydet"] = "Save to Library",
        ["Yüzeye uç ekle"] = "Add Surface Pins",

        // Dock panelleri & Sayfa gezgini
        ["Sayfalar"] = "Pages",
        ["Cihazlar"] = "Devices",
        ["Potansiyeller"] = "Potentials",
        ["Klemensler"] = "Terminals",
        ["PLC"] = "PLC",
        ["Kablolar"] = "Cables",
        ["Semboller"] = "Symbols",
        ["Ağaç"] = "Tree",
        ["Liste"] = "List",
        ["Sayfa ara"] = "Search pages",
        ["Sembol ara"] = "Search symbols",
        ["Seçili cihaza uyanlar"] = "Match selected device",
        ["Görsel sembol seçici…"] = "Visual symbol picker…",
        ["Sembolü düzenle…"] = "Edit symbol…",
        ["Sayfaya yerleştir"] = "Place on page",
        ["Seçili sembole ata"] = "Assign symbol",
        ["Tümü"] = "All",
        ["Numara"] = "Number",
        ["Başlık"] = "Title",
        ["Fonksiyon"] = "Function",
        ["Yer"] = "Location",
        ["Şablon"] = "Template",
        ["ERC temiz"] = "ERC clean",

        // Ortak düğmeler & diyaloglar
        ["Tamam"] = "OK",
        ["İptal"] = "Cancel",
        ["Evet"] = "Yes",
        ["Hayır"] = "No",
        ["Kapat"] = "Close",
        ["Uygula"] = "Apply",
        ["Güncelle"] = "Update",
        ["Ekle"] = "Add",

        // Yapay zeka penceresi
        ["Sağlayıcı"] = "Provider",
        ["Model"] = "Model",
        ["Adres"] = "Endpoint",
        ["API anahtarı"] = "API Key",
        ["Modelleri yenile"] = "Refresh Models",
        ["Değişiklik isteği"] = "Change Request",
        ["Önerilen değişiklikler"] = "Proposed Changes",
        ["Öneri iste"] = "Ask AI",
        ["Yerel Ollama"] = "Local Ollama",
        ["Claude (Anthropic)"] = "Claude (Anthropic)",
        ["Antigravity (Gemini / Proxy)"] = "Antigravity (Gemini / Proxy)",
        ["OpenAI uyumlu uç nokta"] = "OpenAI-compatible endpoint",

        // Kablo ve damar penceresi
        ["Kablolar ve damar atama"] = "Cables & Wire Assignment",
        ["Projedeki kablolar"] = "Project Cables",
        ["Ürün etiketi (-)"] = "Product Tag (-)",
        ["Kablo tipi"] = "Cable Type",
        ["Damarlar (Ad : Kesit : Renk)"] = "Cores (Name : Section : Color)",
        ["Kablo ekle"] = "Add Cable",
        ["Yeni / Temizle"] = "New / Clear",
        ["Bağlantıya tel / kablo damarı ata"] = "Assign Wire / Cable Core to Connection",
        ["Bağlantı seçimi:"] = "Connection:",
        ["Fiziksel tel"] = "Physical wire",
        ["Kablo damarı"] = "Cable core",
        ["Tel numarası"] = "Wire Number",
        ["Kesit (mm²)"] = "Cross-section (mm²)",
        ["Renk"] = "Color",
        ["Renk kartelası"] = "Color Palette",
        ["Çizgi biçimi"] = "Line Style",
        ["Bağlantı türü"] = "Connection Type",
        ["Düz çizgi"] = "Solid",
        ["Kesik çizgili"] = "Dashed",
        ["Çizgi-nokta"] = "Dash-dot",
        ["Noktalı"] = "Dotted",
        ["Kablo damarı seçimi"] = "Cable Core",
        ["Bağlantıya uygula"] = "Apply to Connection",

        // PLC I/O penceresi
        ["PLC Adresi ↔ Tel Çapraz Referans Tablosu"] = "PLC Address ↔ Wire Cross-Reference",
        ["PLC adresine göre sırala"] = "Sort by PLC address",
        ["Tel numarasına göre sırala"] = "Sort by wire number",
        ["CSV adres yapıştır…"] = "Paste CSV addresses…",
        ["CSV dosyasından al…"] = "Import CSV file…",
        ["Yeni PLC ekle (8 DI / 8 DO)"] = "Quick Add PLC (8 DI / 8 DO)",
        ["PLC adresi"] = "PLC Address",
        ["Sembolik ad"] = "Symbol Name",
        ["Açıklama"] = "Description",
        ["Saha function'ı"] = "Field Function",
        ["Kanalı güncelle"] = "Update Channel",
        ["Kanal açıklaması"] = "Channel description",
        ["Örn: %I0.0, bAcilStop, Acil stop kontağı\nveya: DI0; %I0.0; bAcilStop; Acil stop kontağı"] =
            "e.g. %I0.0, bEStop, Emergency stop contact\nor: DI0; %I0.0; bEStop; Emergency stop contact",
        ["Örnek PLC denetleyicisi eklendi (8 DI / 8 DO). Adresleri düzenleyip saha cihazına bağlayabilirsiniz."] =
            "Starter PLC controller added (8 DI / 8 DO). Edit addresses and map field functions.",
        ["Adres listesini uygula"] = "Apply Address List",
        ["PLC kanalı güncellendi."] = "PLC channel(s) updated.",
        ["CSV / noktalı virgül listesi (Adres, SembolikAd, Açıklama veya Kanal, Adres, SembolikAd, Açıklama):"] =
            "CSV / semicolon list (Address, SymbolName, Description or Channel, Address, SymbolName, Description):",
        ["PLC adres listesi (.csv / .txt) aç"] = "Open PLC address list (.csv / .txt)",
        ["Dosya okunamadı"] = "Could not read file",
        ["PLC kanalı dosyadan güncellendi."] = "PLC channel(s) updated from file.",
        ["— (atanmamış)"] = "— (unassigned)",
        ["Projede tanımlı PLC denetleyicisi yok. Sağ üstteki 'Yeni PLC ekle (8 DI / 8 DO)' ile hemen oluşturabilirsiniz."] =
            "No PLC controller defined in the project. Click 'Quick Add PLC (8 DI / 8 DO)' at the top-right to add one.",
        ["Düzenlemek için tablodan bir kanal seçin."] = "Select a channel row from the table to edit.",
        ["PLC Adresi"] = "PLC Address",
        ["Sembolik Ad"] = "Symbol Name",
        ["Sinyal"] = "Signal",
        ["Tel / Damar"] = "Wire / Core",
        ["Klemens"] = "Terminal",
        ["Saha Ucu"] = "Field Pin",
        ["Modül Ucu"] = "Module Pin",
        ["Modül · Açıklama"] = "Module · Description",

        // Bağlantı ve onay mesajları
        ["Tel numaralarını baştan ver"] = "Renumber All Wires",
        ["Projede numaralandırılacak tel veya bağlantı bulunamadı. Önce 'Tel çek' aracıyla iki uç arasında bağlantı oluşturun."] =
            "No wires or connections found to number. Draw connections between two pins first with 'Draw Wire'.",
        ["Projedeki tüm tel numaraları baştan numaralandırılacak ve mevcut numaralar değişecektir. Devam etmek istediğinize emin misiniz?\n\n(Hatalı tıklarsanız Ctrl+Z / Geri Al ile tek adımda geri alabilirsiniz.)"] =
            "All wire numbers in the project will be renumbered from scratch and existing numbers will be overwritten. Are you sure you want to continue?\n\n(If clicked by mistake, you can undo everything in one step via Ctrl+Z / Undo.)",
        ["Baştan numaralandırma iptal edildi."] = "Wire renumbering canceled.",
        ["Tüm teller baştan numaralandı (geri almak için Ctrl+Z)."] = "All wires renumbered (press Ctrl+Z to undo).",
        ["Projede henüz tel bağlantısı yok."] = "No wire connections in the project yet.",
        ["Yeni teller numaralandırıldı."] = "New wires numbered.",
        ["{0} yeni tel numaralandırıldı."] = "{0} new wire(s) numbered.",
        ["Numaralandırılacak yeni tel yok (tüm teller zaten numaralı)."] = "No unnumbered wires left.",
        ["Standart bağlantı türleri zaten projede mevcut."] = "Standard connection types are already present in the project.",
        ["{0} standart bağlantı türü eklendi. 'Kablo ve damar…' penceresinden bağlantılara atayabilirsiniz."] =
            "{0} standard connection type(s) added. Assign them via 'Cables & Wires…'.",
        ["Haberleşme şeması"] = "Communication Diagram",
        ["Tek hat şeması"] = "Single-Line Diagram",
        ["Haberleşme şeması sayfası oluşturuldu."] = "Communication diagram page created.",
        ["Tek hat şeması sayfası oluşturuldu."] = "Single-line diagram page created.",

        // Sağ tık menüleri, Blok editörü, Hakkında ve Durum çubuğu
        ["Bağlantı türü"] = "Connection Type",
        ["Türsüz"] = "None",
        ["Tel / kablo damarı…"] = "Wire / Cable Core…",
        ["Proje özellikleri… (kapak bilgileri)"] = "Project Properties… (Cover Info)",
        ["Sayfa numarasını değiştir…"] = "Change Page Number…",
        ["Arayüz dili güncellendi."] = "Display language updated.",
        ["bulgu"] = "finding(s)",
        ["kaydedilmedi"] = "unsaved",
        ["Açık kaynak"] = "Open Source",
        ["Sembol kaynakları"] = "Symbol Sources",
        ["Kullanılan açık kaynak bileşenler"] = "Open-Source Components Used",
        ["Lisansı göster"] = "View License",
        ["Seçili uç"] = "Selected Pin",
        ["Ad"] = "Name",
        ["Uç türü"] = "Pin Kind",
        ["Kontak"] = "Contact",
        ["Kod / pin"] = "Code / Pin",
        ["Seçili yazı"] = "Selected Text",
        ["Metin"] = "Text",
        ["Boyut (mm)"] = "Size (mm)",
        ["Hizalama"] = "Alignment",
        ["Açı (derece)"] = "Angle (deg)",
        ["İpucu: Çift tıklayarak da metni düzenleyebilirsiniz."] = "Tip: You can also double-click text on canvas to edit.",

        // Yardım penceresi
        ["aktapokus-electric Yardım Kılavuzu"] = "aktapokus-electric User Guide",
        ["İçindekiler"] = "Table of Contents",
        ["Konu, komut veya özellik ara…"] = "Search topics, commands or features…",
        ["Görsel anlatım"] = "Visual Guide",
        ["Adım adım kullanım"] = "Step-by-Step Instructions",
        ["İpuçları"] = "Tips",
        ["Eşleşen yardım konusu bulunamadı."] = "No matching help topics found.",
        ["HTML Yardım Dosyasını Kaydet / Aç…"] = "Save / Open HTML Guide…",
        ["HTML kılavuz kaydedildi"] = "HTML guide saved",
    };

    public static UiLanguage Current { get; set; } = UiLanguage.Turkish;

    public static string LanguageCode => Current == UiLanguage.English ? "en" : "tr";

    public static string T(string turkish) => T(turkish, Current);

    public static string T(string turkish, UiLanguage language) =>
        language == UiLanguage.English && English.TryGetValue(turkish, out var mapped)
            ? mapped
            : turkish;

    public static void Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("language", out var prop)
                && string.Equals(prop.GetString(), "en", StringComparison.OrdinalIgnoreCase))
            {
                Current = UiLanguage.English;
            }
            else
            {
                Current = UiLanguage.Turkish;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Current = UiLanguage.Turkish;
        }
    }

    public static void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var json = JsonSerializer.Serialize(new { language = LanguageCode }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tercih kaydedilemezse oturum dilini korumaya devam et.
        }
    }
}
