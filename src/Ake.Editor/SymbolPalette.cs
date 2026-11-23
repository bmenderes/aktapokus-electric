using System.Text.RegularExpressions;

namespace Ake.Editor;

/// <summary>Palette ağacında bir grup: ad, alt gruplar ve semboller. <c>Expanded</c>, arama yapılırken grupların açık gelmesini sağlar.</summary>
public sealed record SymbolGroup(string Name, IReadOnlyList<object> Items, bool Expanded, string CategoryPath = "")
{
    /// <summary>Gruptaki bütün semboller, alt gruplar dahil.</summary>
    public int Count => Items.Sum(item => item is SymbolGroup group ? group.Count : 1);

    public override string ToString() => $"{Name}  ({Count})";
}

/// <summary>
/// Sembol listesini kategorilere ayırır (ADR-0035). Standart setin kategorileri olduğu gibi gelir; üretici kütüphanelerinin klasör adları
/// (<c>fav0100_contact/fav0100_contact_NO_normal_open</c>) okunur Türkçe gruplara çevrilir, aynı ada düşen gruplar birleşir.
/// </summary>
public static partial class SymbolPalette
{
    /// <summary>Kullanıcının kendi (blok editöründe değiştirdiği ya da çizdiği) sembollerinin kategorisi.</summary>
    public const string UserCategory = "Kendi sembollerim";

    private static readonly string[] Order =
    [
        UserCategory, "Kumanda", "Sinyal", "Koruma", "Röle ve kontaktör", "Motor ve aktüatör", "Güç", "Klemens ve konnektör", "Sensör", "Otomasyon", "Pnömatik",
    ];

    // Klasör adındaki anahtar → okunur ad. Sıra önemlidir: ilk eşleşen kazanır.
    private static readonly (string Key, string Name)[] Folders =
    [
        ("miscellaneous", "Çeşitli semboller"),
        ("legend_passing", "Geçişli"),
        ("contact_unusual", "Özel kontaklar"),
        ("contact_no_", "Normalde açık (NO)"),
        ("contact_nc_", "Normalde kapalı (NC)"),
        ("change_over", "Değiştirmeli (CO)"),
        ("time_on_off", "Zaman kontakları"),
        ("_contact", "Kontaklar"),
        ("relay_coil", "Röle bobinleri"),
        ("contactor_aux_relay", "Kontaktör ve yardımcı röle"),
        ("_a20_relay", "Röle"),
        ("relay_contact_image", "Röle ve kontaktör görüntüleri"),
        ("push_button", "Butonlar ve anahtarlar"),
        ("signaling_measure", "Sinyal ve ölçü"),
        ("sensor_limit", "Sensör ve sınır anahtarı"),
        ("time_switch", "Zaman anahtarı ve merdiven"),
        ("_motor", "Motorlar"),
        ("rlc_component", "R, L, C elemanları"),
        ("semiconductor", "Yarı iletkenler"),
        ("transformer", "Trafo ve güç kaynağı"),
        ("circuit_breaker", "Devre kesici ve RCD"),
        ("electromechanical", "Elektromekanik"),
        ("fuse", "Sigorta ve parafudr"),
        ("plug_and_socket", "Fiş ve priz"),
        ("electrical_terminal", "Klemens ve konnektör"),
        ("bloc_pneumatic", "Pnömatik (DXF)"),
        ("diagram_connection", "Bağlantı şemaları"),
        ("single_line_diagram", "Tek hat şeması"),
        ("plc_starter_converter", "PLC, yol verici ve sürücü"),
        ("plc_logo", "PLC modülleri"),
        ("soft_starter", "Yumuşak yol verici"),
        ("frequency_converter", "Frekans konvertörü"),
        ("safety_relay", "Emniyet rölesi"),
        ("proximity_switch", "Yakınlık sensörleri"),
        ("blackbox", "Cihaz kutuları"),
        ("connection_cable", "Kablo bağlantıları"),
        ("connection_device", "Cihaz bağlantıları"),
        ("limit_switch", "Sınır anahtarları"),
    ];

    private static readonly Dictionary<string, string> QetFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["10_electric"] = "Elektrik (QElectroTech)",
        ["qelectrotech"] = "Örnek QElectroTech",
        ["10_allpole"] = "Çok hatlı (All-pole)",
        ["11_singlepole"] = "Tek hat (Single-pole)",
        ["20_manufacturers_articles"] = "Üretici sembolleri",
        ["90_american_standards"] = "NEMA / Amerikan standartları",
        ["91_en_60617"] = "IEC / EN 60617",
        ["98_graphics"] = "Grafik ve etiketler",
        ["99_miscellaneous_unsorted"] = "Çeşitli",
        ["100_folio_referencing"] = "Sayfa referansları",
        ["110_network_supplies"] = "Şebeke ve kaynaklar",
        ["114_connections"] = "Bağlantılar",
        ["120_cables_wiring"] = "Kablo ve teller",
        ["130_terminals_terminal_strips"] = "Klemensler",
        ["140_connectors_plugs"] = "Konnektör ve fişler",
        ["200_fuses_protective_gears"] = "Sigorta ve koruma",
        ["310_relays_contactors_contacts"] = "Röle, kontaktör ve kontaklar",
        ["330_transformers_power_supplies"] = "Trafo ve güç kaynakları",
        ["340_converters_inverters"] = "Konvertör ve sürücüler",
        ["380_signaling_operating"] = "Kumanda ve sinyal",
        ["390_sensors_instruments"] = "Sensör ve ölçü aletleri",
        ["391_consumers_actuators"] = "Motor ve aktüatörler",
        ["392_generators_sources"] = "Jeneratör ve kaynaklar",
        ["395_electronics_semiconductors"] = "Elektronik ve yarı iletkenler",
        ["450_high_voltage"] = "Yüksek gerilim",
        ["500_home_installation"] = "Bina tesisatı",
    };

    /// <summary>Semboller sırayla gruplara konur: kendi semboller, standart set, sonra diğerleri alfabetik.</summary>
    public static IReadOnlyList<SymbolGroup> Build(
        IEnumerable<SymbolRow> rows,
        bool expanded,
        IEnumerable<string>? extraCategories = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var root = new Node(string.Empty);
        foreach (var row in rows)
        {
            var node = root;
            var segments = (row.Category ?? "Diğer").Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments.Length == 0 ? ["Diğer"] : segments)
            {
                node = node.Child(FriendlyFolder(segment));
            }

            node.Rows.Add(row);
        }

        if (extraCategories is not null)
        {
            foreach (var category in extraCategories)
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    continue;
                }

                var node = root;
                foreach (var segment in category.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    node = node.Child(FriendlyFolder(segment));
                }
            }
        }

        return [.. root.Children.OrderBy(child => RankOf(child.Name)).ThenBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(child => child.ToGroup(expanded))];
    }

    /// <summary>Üretici dosya adından okunur ad: <c>fav0100_A10_contact_NO_detent</c> → <c>Contact NO detent</c>.</summary>
    public static string CleanName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var text = name[(name.LastIndexOf('/') + 1)..];
        text = Prefix().Replace(text, string.Empty);
        text = Code().Replace(text, string.Empty).Replace('_', ' ').Trim();
        return text.Length == 0 ? name : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Klasör adının okunur karşılığı; tanınmayan ad olduğu gibi kalır.</summary>
    public static string FriendlyFolder(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        var lower = segment.ToLowerInvariant();
        if (!lower.StartsWith("fav", StringComparison.Ordinal))
        {
            return QetFolders.TryGetValue(lower, out var qetName) ? qetName : segment;
        }

        foreach (var (key, name) in Folders)
        {
            if (lower.Contains(key, StringComparison.Ordinal) || lower.EndsWith(key.TrimEnd('_'), StringComparison.Ordinal))
            {
                return name;
            }
        }

        return Prefix().Replace(segment, string.Empty).Replace('_', ' ');
    }

    private static int RankOf(string name)
    {
        var index = Array.IndexOf(Order, name);
        return index < 0 ? int.MaxValue : index;
    }

    [GeneratedRegex(@"^fav\d+[A-Za-z]*_", RegexOptions.IgnoreCase)]
    private static partial Regex Prefix();

    [GeneratedRegex(@"^[A-Za-z]\d+[A-Za-z]*_")]
    private static partial Regex Code();

    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public List<Node> Children { get; } = [];

        public List<SymbolRow> Rows { get; } = [];

        public Node Child(string childName)
        {
            var found = Children.Find(child => string.Equals(child.Name, childName, StringComparison.CurrentCultureIgnoreCase));
            if (found is null)
            {
                found = new Node(childName);
                Children.Add(found);
            }

            return found;
        }

        public SymbolGroup ToGroup(bool expanded, string parentPath = "")
        {
            var path = string.IsNullOrEmpty(parentPath) ? Name : $"{parentPath}/{Name}";
            return new(
                Name,
                [
                    .. Children.OrderBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase).Select(child => (object)child.ToGroup(expanded, path)),
                    .. Rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
                ],
                expanded,
                path);
        }
    }
}
