using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Model;

namespace Ake.Library;

internal static class ManufacturerImportSupport
{
    public static Error? CheckAttribution(string source, string license)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new Error("kutuphane.kaynak_yok", "Kaynak (source) belirtilmeden üretici kütüphanesi içe aktarılamaz.");
        }

        if (string.IsNullOrWhiteSpace(license))
        {
            return new Error("kutuphane.lisans_yok", "Lisans (license) belirtilmeden üretici kütüphanesi içe aktarılamaz.");
        }

        return null;
    }

    public static Result<XDocument> LoadXml(string content, string context)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024,
                MaxCharactersInDocument = 50_000_000,
            };
            using var reader = XmlReader.Create(new StringReader(content), settings);
            return Result.Success(XDocument.Load(reader));
        }
        catch (Exception ex) when (ex is XmlException or FormatException)
        {
            return Result.Failure<XDocument>(new Error("kutuphane.xml_hatali", $"{context} XML okunamadı: {ex.Message}"));
        }
    }

    public static string ReadZipEntryText(ZipArchiveEntry entry, long maxBytes = 50L * 1024 * 1024)
    {
        using var src = entry.Open();
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException($"ZIP girdisi boyut sınırını ({maxBytes / (1024 * 1024)} MB) aşıyor: '{entry.FullName}'.");
            }

            ms.Write(buffer, 0, read);
        }

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static IEnumerable<XElement> ElementsNamed(this XContainer container, string localName) =>
        container.Descendants().Where(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    public static XElement? ElementNamed(this XContainer container, string localName) =>
        container.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    public static string? TextOf(this XContainer container, params string[] names)
    {
        foreach (var name in names)
        {
            var found = container.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(e.Value));
            if (found is not null)
            {
                return found.Value.Trim();
            }
        }

        return null;
    }

    public static string? Attr(this XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var a = element.Attributes()
                .FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(x.Value));
            if (a is not null)
            {
                return a.Value.Trim();
            }
        }

        return null;
    }

    public static decimal? ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(text, @"-?\d+(?:[.,]\d+)?");
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Value.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static string FallbackText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static Result<string> ReadTextFile(string path)
    {
        if (!File.Exists(path))
        {
            return Result.Failure<string>(new Error("kutuphane.dosya_yok", $"Dosya bulunamadı: '{path}'"));
        }

        try
        {
            return Result.Success(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<string>(new Error("kutuphane.okunamadi", $"'{path}' okunamadı: {ex.Message}"));
        }
    }

    public static bool IsZipFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[2];
            return stream.Read(header) == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
        }
        catch (IOException)
        {
            return false;
        }
    }
}

/// <summary>
/// BMEcat (1.2 / 2005) ürün kataloğu ve ETIM sınıflandırmasından ürün + fonksiyon şablonu içe aktarır (ADR-0059).
/// </summary>
public static class BmecatImporter
{
    public static Result<IReadOnlyList<LibraryProduct>> FromFile(string path, string source, string license)
    {
        var text = ManufacturerImportSupport.ReadTextFile(path);
        return text.IsFailure ? Result.Failure<IReadOnlyList<LibraryProduct>>(text.Error!) : FromXml(text.Value, source, license);
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromXml(string xml, string source, string license)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        var loaded = ManufacturerImportSupport.LoadXml(xml, "BMEcat");
        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(loaded.Error!);
        }

        var doc = loaded.Value;
        var supplierName = doc.ElementsNamed("SUPPLIER_NAME").FirstOrDefault()?.Value.Trim();
        var items = doc.ElementsNamed("ARTICLE").Concat(doc.ElementsNamed("PRODUCT")).ToList();
        var products = new List<LibraryProduct>(items.Count);

        foreach (var item in items)
        {
            var orderCode = item.TextOf("SUPPLIER_AID", "SUPPLIER_PID", "MANUFACTURER_AID", "MANUFACTURER_PID");
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                continue;
            }

            var manufacturer = ManufacturerImportSupport.FallbackText(
                item.TextOf("MANUFACTURER_NAME") ?? supplierName,
                source.Trim());
            var shortDesc = item.TextOf("DESCRIPTION_SHORT");
            var longDesc = item.TextOf("DESCRIPTION_LONG");
            var typeDesc = item.TextOf("MANUFACTURER_TYPE_DESCR", "MANUFACTURER_TYPE_DESC") ?? shortDesc;

            string? etimClass = null;
            decimal? ratedCurrentA = null;
            var props = new List<ProductProperty>();

            foreach (var featureBlock in item.ElementsNamed("ARTICLE_FEATURES").Concat(item.ElementsNamed("PRODUCT_FEATURES")))
            {
                var system = featureBlock.TextOf("REFERENCE_FEATURE_SYSTEM_NAME");
                var group = featureBlock.TextOf("REFERENCE_FEATURE_GROUP_ID");
                if (!string.IsNullOrWhiteSpace(group) && (system is null || system.Contains("ETIM", StringComparison.OrdinalIgnoreCase) || group.StartsWith("EC", StringComparison.OrdinalIgnoreCase)))
                {
                    etimClass ??= group;
                    props.Add(new ProductProperty("ETIM", group));
                }

                foreach (var feature in featureBlock.ElementsNamed("FEATURE"))
                {
                    var fname = feature.TextOf("FNAME");
                    var fval = feature.TextOf("FVALUE");
                    var funit = feature.TextOf("FUNIT");
                    if (string.IsNullOrWhiteSpace(fname) || string.IsNullOrWhiteSpace(fval))
                    {
                        continue;
                    }

                    props.Add(new ProductProperty(fname, fval, ManufacturerImportSupport.Clean(funit)));
                    if (ratedCurrentA is null && (fname.Contains("akım", StringComparison.OrdinalIgnoreCase)
                        || fname.Contains("current", StringComparison.OrdinalIgnoreCase)
                        || fname.Contains("strom", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(funit, "A", StringComparison.OrdinalIgnoreCase)))
                    {
                        ratedCurrentA = ManufacturerImportSupport.ParseDecimal(fval);
                    }
                }
            }

            var templates = EtimTemplateMapper.TemplatesForClass(etimClass, shortDesc ?? longDesc, ratedCurrentA);
            var category = EtimTemplateMapper.CategoryForClass(etimClass, shortDesc);
            var def = new ProductDefinition(
                manufacturer,
                orderCode,
                templates,
                ManufacturerImportSupport.Clean(typeDesc),
                ManufacturerImportSupport.Clean(longDesc ?? shortDesc));
            products.Add(new LibraryProduct(def, source.Trim(), license.Trim(), category, props));
        }

        return products.Count == 0
            ? Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.bmecat_bos", "BMEcat dosyasında geçerli ürün bulunamadı."))
            : Result.Success<IReadOnlyList<LibraryProduct>>(products);
    }
}

/// <summary>
/// PROFINET GSDML (ISO 15745) XML cihaz tanımından PLC/fieldbus modülü ve I/O kanalı içe aktarır (ADR-0059).
/// </summary>
public static class GsdmlImporter
{
    public static Result<IReadOnlyList<LibraryProduct>> FromFile(string path, string source, string license)
    {
        var text = ManufacturerImportSupport.ReadTextFile(path);
        return text.IsFailure ? Result.Failure<IReadOnlyList<LibraryProduct>>(text.Error!) : FromXml(text.Value, source, license);
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromXml(string xml, string source, string license)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        var loaded = ManufacturerImportSupport.LoadXml(xml, "GSDML");
        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(loaded.Error!);
        }

        var doc = loaded.Value;
        var identity = doc.ElementsNamed("DeviceIdentity").FirstOrDefault();
        if (identity is null)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.gsdml_gecersiz", "GSDML DeviceIdentity bölümü bulunamadı."));
        }

        var vendor = ManufacturerImportSupport.FallbackText(identity.Attr("VendorName"), source.Trim());
        var deviceId = identity.Attr("DeviceID");
        var vendorId = identity.Attr("VendorID");
        var textLookup = BuildExternalTextLookup(doc);
        var infoText = ResolveText(identity.ElementNamed("InfoText"), textLookup);

        var products = new List<LibraryProduct>();
        var items = doc.ElementsNamed("DeviceAccessPointItem").Concat(doc.ElementsNamed("ModuleItem")).ToList();

        foreach (var item in items)
        {
            var moduleInfo = item.ElementNamed("ModuleInfo");
            var orderEl = moduleInfo?.ElementNamed("OrderNumber");
            var nameEl = moduleInfo?.ElementNamed("Name");
            var descEl = moduleInfo?.ElementNamed("InfoText");

            var itemName = ResolveText(nameEl, textLookup);
            var orderCode = orderEl?.Attr("Value") ?? item.Attr("ID") ?? itemName;
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                continue;
            }

            var itemDesc = ResolveText(descEl, textLookup) ?? infoText;
            var ioPins = ExtractIoPins(item, textLookup);
            var functions = new List<FunctionTemplate>();
            if (ioPins.Count > 0)
            {
                functions.Add(new FunctionTemplate(FunctionKind.PlcChannel, ioPins, Description: itemName ?? "PROFINET I/O"));
            }
            else
            {
                functions.Add(new FunctionTemplate(FunctionKind.PlcChannel, ["P1", "P2", "24V", "0V"], Description: itemName ?? "PROFINET DAP"));
            }

            var props = new List<ProductProperty>();
            if (vendorId is not null)
            {
                props.Add(new ProductProperty("PROFINET VendorID", vendorId));
            }

            if (deviceId is not null)
            {
                props.Add(new ProductProperty("PROFINET DeviceID", deviceId));
            }

            var hwRev = moduleInfo?.ElementNamed("HardwareRelease")?.Attr("Value");
            if (hwRev is not null)
            {
                props.Add(new ProductProperty("HardwareRelease", hwRev));
            }

            var swRev = moduleInfo?.ElementNamed("SoftwareRelease")?.Attr("Value");
            if (swRev is not null)
            {
                props.Add(new ProductProperty("SoftwareRelease", swRev));
            }

            var def = new ProductDefinition(
                vendor,
                orderCode.Trim(),
                functions,
                ManufacturerImportSupport.Clean(itemName),
                ManufacturerImportSupport.Clean(itemDesc));
            products.Add(new LibraryProduct(def, source.Trim(), license.Trim(), "Otomasyon / PROFINET", props));
        }

        if (products.Count == 0)
        {
            var fallbackOrder = identity.TextOf("InfoText") ?? deviceId ?? "PROFINET-Device";
            products.Add(new LibraryProduct(
                new ProductDefinition(
                    vendor,
                    fallbackOrder,
                    [new(FunctionKind.PlcChannel, ["P1", "P2", "24V", "0V"], Description: infoText)],
                    infoText,
                    infoText),
                source.Trim(),
                license.Trim(),
                "Otomasyon / PROFINET",
                []));
        }

        return Result.Success<IReadOnlyList<LibraryProduct>>(products);
    }

    private static Dictionary<string, string> BuildExternalTextLookup(XDocument doc)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in doc.ElementsNamed("Text"))
        {
            var id = text.Attr("TextId");
            var val = text.Attr("Value") ?? ManufacturerImportSupport.Clean(text.Value);
            if (id is not null && val is not null)
            {
                map.TryAdd(id, val);
            }
        }

        return map;
    }

    private static string? ResolveText(XElement? element, IReadOnlyDictionary<string, string> lookup)
    {
        if (element is null)
        {
            return null;
        }

        var textId = element.Attr("TextId");
        if (textId is not null && lookup.TryGetValue(textId, out var resolved))
        {
            return resolved;
        }

        return ManufacturerImportSupport.Clean(element.Attr("Value") ?? element.Value);
    }

    private static IReadOnlyList<string> ExtractIoPins(XElement moduleItem, IReadOnlyDictionary<string, string> lookup)
    {
        var pins = new List<string>();
        foreach (var dataItem in moduleItem.ElementsNamed("DataItem"))
        {
            var label = ResolveText(dataItem, lookup);
            var fallback = $"CH{pins.Count}";
            pins.Add(string.IsNullOrWhiteSpace(label) ? fallback : label);
            if (pins.Count >= 32)
            {
                break;
            }
        }

        return pins;
    }
}

/// <summary>
/// EtherNet/IP / CIP / CANopen Electronic Data Sheet (EDS) INI dosyasından ürün içe aktarır (ADR-0059).
/// </summary>
public static class EdsImporter
{
    public static Result<IReadOnlyList<LibraryProduct>> FromFile(string path, string source, string license)
    {
        var text = ManufacturerImportSupport.ReadTextFile(path);
        return text.IsFailure ? Result.Failure<IReadOnlyList<LibraryProduct>>(text.Error!) : FromText(text.Value, source, license);
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromText(string content, string source, string license)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        var sections = ParseIniSections(content);
        if (!sections.TryGetValue("Device", out var device) && !sections.TryGetValue("DeviceInfo", out device))
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.eds_gecersiz", "EDS dosyasında [Device] bölümü bulunamadı."));
        }

        var vendor = Unquote(device.GetValueOrDefault("VendName")) ?? Unquote(device.GetValueOrDefault("VendorName")) ?? source.Trim();
        var prodName = Unquote(device.GetValueOrDefault("ProdName")) ?? Unquote(device.GetValueOrDefault("ProductName"));
        var catalog = Unquote(device.GetValueOrDefault("Catalog")) ?? prodName ?? Unquote(device.GetValueOrDefault("ProductCode")) ?? "EDS-Device";
        var description = Unquote(sections.GetValueOrDefault("File")?.GetValueOrDefault("DescText")) ?? prodName;

        var props = new List<ProductProperty>();
        foreach (var key in new[] { "VendCode", "VendorNumber", "ProdTypeStr", "ProdCode", "ProductNumber", "MajRev", "MinRev", "RevisionNumber" })
        {
            if (device.TryGetValue(key, out var val) && Unquote(val) is { } cleanVal)
            {
                props.Add(new ProductProperty(key, cleanVal));
            }
        }

        if (sections.TryGetValue("Params", out var paramSec))
        {
            foreach (var (k, v) in paramSec)
            {
                if (!k.StartsWith("Param", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = v.Split(',');
                if (parts.Length >= 8)
                {
                    var pName = Unquote(parts[7]);
                    var pUnit = parts.Length >= 9 ? Unquote(parts[8]) : null;
                    if (pName is not null)
                    {
                        props.Add(new ProductProperty(pName, parts[6].Trim(), pUnit));
                    }
                }
            }
        }

        var functions = new List<FunctionTemplate>
        {
            new(FunctionKind.PlcChannel, ["ETH1", "ETH2", "24V", "0V"], Description: prodName ?? "EtherNet/IP cihazı"),
        };

        var product = new LibraryProduct(
            new ProductDefinition(vendor, catalog, functions, prodName, description),
            source.Trim(),
            license.Trim(),
            "Otomasyon / EtherNet-IP",
            props);
        return Result.Success<IReadOnlyList<LibraryProduct>>([product]);
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIniSections(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;
        var logical = new StringBuilder();

        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                FlushEntry(current, logical);
                var name = line[1..^1].Trim();
                if (!result.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result[name] = current;
                }

                continue;
            }

            if (line.EndsWith(';'))
            {
                logical.Append(line[..^1].TrimEnd());
                FlushEntry(current, logical);
            }
            else
            {
                logical.Append(line).Append(' ');
            }
        }

        FlushEntry(current, logical);
        return result;
    }

    private static void FlushEntry(Dictionary<string, string>? section, StringBuilder logical)
    {
        if (logical.Length == 0)
        {
            return;
        }

        var text = logical.ToString().Trim();
        logical.Clear();
        if (section is null)
        {
            return;
        }

        var eq = text.IndexOf('=');
        if (eq > 0)
        {
            section[text[..eq].Trim()] = text[(eq + 1)..].Trim();
        }
    }

    private static string StripComment(string line)
    {
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote && line[i] == '$')
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string? Unquote(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var t = raw.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
        {
            t = t[1..^1];
        }

        t = t.Trim();
        return t.Length == 0 ? null : t;
    }
}

/// <summary>
/// IO-Link IODD XML (veya zip paketli .iodd) cihaz tanımından sensör/aktüatör içe aktarır (ADR-0059).
/// </summary>
public static class IoddImporter
{
    public static Result<IReadOnlyList<LibraryProduct>> FromFile(string path, string source, string license)
    {
        if (!File.Exists(path))
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.dosya_yok", $"Dosya bulunamadı: '{path}'"));
        }

        if (ManufacturerImportSupport.IsZipFile(path))
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var xmlEntry = zip.Entries
                    .Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !e.Name.EndsWith("-de.xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.Ordinal)
                    .FirstOrDefault()
                    ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
                if (xmlEntry is null)
                {
                    return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.iodd_bos", "IODD paketinde XML tanımı bulunamadı."));
                }

                return FromXml(ManufacturerImportSupport.ReadZipEntryText(xmlEntry), source, license);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.okunamadi", $"'{path}' paketi okunamadı: {ex.Message}"));
            }
        }

        var text = ManufacturerImportSupport.ReadTextFile(path);
        return text.IsFailure ? Result.Failure<IReadOnlyList<LibraryProduct>>(text.Error!) : FromXml(text.Value, source, license);
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromXml(string xml, string source, string license)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        var loaded = ManufacturerImportSupport.LoadXml(xml, "IODD");
        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(loaded.Error!);
        }

        var doc = loaded.Value;
        var identity = doc.ElementsNamed("DeviceIdentity").FirstOrDefault();
        if (identity is null)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.iodd_gecersiz", "IODD DeviceIdentity bölümü bulunamadı."));
        }

        var vendor = ManufacturerImportSupport.FallbackText(identity.Attr("vendorName"), source.Trim());
        var vendorId = identity.Attr("vendorId");
        var deviceId = identity.Attr("deviceId");
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in doc.ElementsNamed("Text"))
        {
            var id = t.Attr("id");
            var v = t.Attr("value");
            if (id is not null && v is not null)
            {
                texts.TryAdd(id, v);
            }
        }

        var deviceName = ResolveIoddText(identity.ElementNamed("DeviceName"), texts);
        var props = new List<ProductProperty>();
        if (vendorId is not null)
        {
            props.Add(new ProductProperty("IO-Link VendorID", vendorId));
        }

        if (deviceId is not null)
        {
            props.Add(new ProductProperty("IO-Link DeviceID", deviceId));
        }

        var pdIn = doc.ElementsNamed("ProcessDataIn").FirstOrDefault()?.Attr("bitLength");
        var pdOut = doc.ElementsNamed("ProcessDataOut").FirstOrDefault()?.Attr("bitLength");
        if (pdIn is not null)
        {
            props.Add(new ProductProperty("ProcessDataIn", pdIn, "bit"));
        }

        if (pdOut is not null)
        {
            props.Add(new ProductProperty("ProcessDataOut", pdOut, "bit"));
        }

        var pins = new List<string> { "L+", "C/Q", "L-" };
        if (doc.ElementsNamed("Pin2").Any())
        {
            pins.Add("DI/DO");
        }

        var products = new List<LibraryProduct>();
        foreach (var variant in doc.ElementsNamed("DeviceVariant"))
        {
            var productId = variant.Attr("productId");
            if (string.IsNullOrWhiteSpace(productId))
            {
                continue;
            }

            var vName = ResolveIoddText(variant.ElementNamed("Name"), texts) ?? deviceName;
            var vDesc = ResolveIoddText(variant.ElementNamed("Description"), texts);
            var def = new ProductDefinition(
                vendor,
                productId,
                [new FunctionTemplate(FunctionKind.Generic, pins, Description: vName ?? "IO-Link cihazı")],
                vName,
                vDesc);
            products.Add(new LibraryProduct(def, source.Trim(), license.Trim(), "Sensör / IO-Link", props));
        }

        if (products.Count == 0)
        {
            var order = deviceName ?? deviceId ?? "IODD-Device";
            products.Add(new LibraryProduct(
                new ProductDefinition(
                    vendor,
                    order,
                    [new FunctionTemplate(FunctionKind.Generic, pins, Description: deviceName ?? "IO-Link cihazı")],
                    deviceName,
                    null),
                source.Trim(),
                license.Trim(),
                "Sensör / IO-Link",
                props));
        }

        return Result.Success<IReadOnlyList<LibraryProduct>>(products);
    }

    private static string? ResolveIoddText(XElement? element, IReadOnlyDictionary<string, string> texts)
    {
        if (element is null)
        {
            return null;
        }

        var tid = element.Attr("textId");
        return tid is not null && texts.TryGetValue(tid, out var val) ? val : ManufacturerImportSupport.Clean(element.Value);
    }
}

/// <summary>
/// Asset Administration Shell (.aasx paketi, AAS XML veya AAS JSON) varlık tanımlarından ürün ve teknik özellik içe aktarır (ADR-0059).
/// </summary>
public static class AasxImporter
{
    public static Result<IReadOnlyList<LibraryProduct>> FromFile(string path, string source, string license)
    {
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        if (!File.Exists(path))
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.dosya_yok", $"Dosya bulunamadı: '{path}'"));
        }

        if (ManufacturerImportSupport.IsZipFile(path) || Path.GetExtension(path).Equals(".aasx", StringComparison.OrdinalIgnoreCase))
        {
            return FromAasxPackage(path, source, license);
        }

        var text = ManufacturerImportSupport.ReadTextFile(path);
        if (text.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(text.Error!);
        }

        return text.Value.TrimStart().StartsWith('{')
            ? FromJson(text.Value, source, license)
            : FromXml(text.Value, source, license);
    }

    private static Result<IReadOnlyList<LibraryProduct>> FromAasxPackage(string path, string source, string license)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var candidates = zip.Entries
                .Where(e => !e.FullName.Contains("_rels/", StringComparison.OrdinalIgnoreCase)
                    && !e.Name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
                    && (e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(e => e.FullName, StringComparer.Ordinal)
                .ToList();

            var products = new List<LibraryProduct>();
            foreach (var entry in candidates)
            {
                var payload = ManufacturerImportSupport.ReadZipEntryText(entry);
                var parsed = payload.TrimStart().StartsWith('{')
                    ? FromJson(payload, source, license)
                    : FromXml(payload, source, license);
                if (parsed.IsSuccess)
                {
                    products.AddRange(parsed.Value);
                }
            }

            return products.Count == 0
                ? Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.aasx_bos", "AASX paketinde geçerli AAS ürün tanımı bulunamadı."))
                : Result.Success<IReadOnlyList<LibraryProduct>>(products);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.okunamadi", $"'{path}' AASX açılamadı: {ex.Message}"));
        }
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromXml(string xml, string source, string license)
    {
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        var loaded = ManufacturerImportSupport.LoadXml(xml, "AAS");
        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(loaded.Error!);
        }

        var doc = loaded.Value;
        var props = new List<ProductProperty>();
        string? manufacturer = null;
        string? orderCode = null;
        string? typeDesignation = null;
        string? description = null;
        string? etimClass = null;

        foreach (var prop in doc.ElementsNamed("property").Concat(doc.ElementsNamed("Property")))
        {
            var idShort = prop.TextOf("idShort");
            var val = prop.TextOf("value");
            if (string.IsNullOrWhiteSpace(idShort) || string.IsNullOrWhiteSpace(val))
            {
                continue;
            }

            props.Add(new ProductProperty(idShort, val));
            AssignKnownAasField(idShort, val, ref manufacturer, ref orderCode, ref typeDesignation, ref description, ref etimClass);
        }

        var shellName = doc.ElementsNamed("assetAdministrationShell").FirstOrDefault()?.TextOf("idShort")
            ?? doc.ElementsNamed("asset").FirstOrDefault()?.TextOf("idShort");
        orderCode ??= typeDesignation ?? shellName;
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.aas_bos", "AAS XML içinde ürün kodu veya Nameplate bilgisi bulunamadı."));
        }

        var templates = EtimTemplateMapper.TemplatesForClass(etimClass, description ?? typeDesignation);
        var category = EtimTemplateMapper.CategoryForClass(etimClass, "AAS Varlığı");
        var def = new ProductDefinition(
            ManufacturerImportSupport.FallbackText(manufacturer, source.Trim()),
            orderCode,
            templates,
            ManufacturerImportSupport.Clean(typeDesignation ?? shellName),
            ManufacturerImportSupport.Clean(description));
        return Result.Success<IReadOnlyList<LibraryProduct>>([new LibraryProduct(def, source.Trim(), license.Trim(), category, props)]);
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromJson(string json, string source, string license)
    {
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var props = new List<ProductProperty>();
            string? manufacturer = null;
            string? orderCode = null;
            string? typeDesignation = null;
            string? description = null;
            string? etimClass = null;

            CollectJsonProps(doc.RootElement, props, ref manufacturer, ref orderCode, ref typeDesignation, ref description, ref etimClass);
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                orderCode = typeDesignation;
            }

            if (string.IsNullOrWhiteSpace(orderCode))
            {
                return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.aas_bos", "AAS JSON içinde ürün tanımı bulunamadı."));
            }

            var templates = EtimTemplateMapper.TemplatesForClass(etimClass, description ?? typeDesignation);
            var category = EtimTemplateMapper.CategoryForClass(etimClass, "AAS Varlığı");
            var def = new ProductDefinition(
                ManufacturerImportSupport.FallbackText(manufacturer, source.Trim()),
                orderCode,
                templates,
                ManufacturerImportSupport.Clean(typeDesignation),
                ManufacturerImportSupport.Clean(description));
            return Result.Success<IReadOnlyList<LibraryProduct>>([new LibraryProduct(def, source.Trim(), license.Trim(), category, props)]);
        }
        catch (JsonException ex)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.json_hatali", $"AAS JSON okunamadı: {ex.Message}"));
        }
    }

    private static void CollectJsonProps(
        JsonElement el,
        List<ProductProperty> props,
        ref string? manufacturer,
        ref string? orderCode,
        ref string? typeDesignation,
        ref string? description,
        ref string? etimClass)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("idShort", out var idEl) && idEl.ValueKind == JsonValueKind.String
                && el.TryGetProperty("value", out var valEl) && valEl.ValueKind == JsonValueKind.String)
            {
                var k = idEl.GetString()!;
                var v = valEl.GetString()!;
                if (!string.IsNullOrWhiteSpace(v))
                {
                    props.Add(new ProductProperty(k, v));
                    AssignKnownAasField(k, v, ref manufacturer, ref orderCode, ref typeDesignation, ref description, ref etimClass);
                }
            }

            foreach (var p in el.EnumerateObject())
            {
                CollectJsonProps(p.Value, props, ref manufacturer, ref orderCode, ref typeDesignation, ref description, ref etimClass);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                CollectJsonProps(item, props, ref manufacturer, ref orderCode, ref typeDesignation, ref description, ref etimClass);
            }
        }
    }

    private static void AssignKnownAasField(
        string idShort,
        string val,
        ref string? manufacturer,
        ref string? orderCode,
        ref string? typeDesignation,
        ref string? description,
        ref string? etimClass)
    {
        if (idShort.Contains("ManufacturerName", StringComparison.OrdinalIgnoreCase))
        {
            manufacturer ??= val;
        }
        else if (idShort.Contains("OrderCode", StringComparison.OrdinalIgnoreCase)
            || idShort.Contains("ManufacturerProductCode", StringComparison.OrdinalIgnoreCase)
            || idShort.Contains("ArticleNumber", StringComparison.OrdinalIgnoreCase))
        {
            orderCode ??= val;
        }
        else if (idShort.Contains("ProductType", StringComparison.OrdinalIgnoreCase)
            || idShort.Contains("ManufacturerProductDesignation", StringComparison.OrdinalIgnoreCase))
        {
            typeDesignation ??= val;
        }
        else if (idShort.Contains("Description", StringComparison.OrdinalIgnoreCase))
        {
            description ??= val;
        }
        else if (idShort.Contains("ETIM", StringComparison.OrdinalIgnoreCase) && val.StartsWith("EC", StringComparison.OrdinalIgnoreCase))
        {
            etimClass ??= val;
        }
    }
}

/// <summary>
/// Deneysel EDZ paketi veya ham part.xml dosyasından parça ve fonksiyon şablonu içe aktarır (ADR-0059).
/// </summary>
public static class EdzImporter
{
    public static Result<IReadOnlyList<LibraryProduct>> FromFile(string path, string source, string license)
    {
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        if (!File.Exists(path))
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.dosya_yok", $"Dosya bulunamadı: '{path}'"));
        }

        if (ManufacturerImportSupport.IsZipFile(path) || Path.GetExtension(path).Equals(".edz", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var xmlEntries = zip.Entries
                    .Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && e.Name.Contains("part", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.Ordinal)
                    .ToList();
                if (xmlEntries.Count == 0)
                {
                    xmlEntries = [.. zip.Entries
                        .Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !e.Name.Equals("manifest.xml", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(e => e.FullName, StringComparer.Ordinal)];
                }

                var all = new List<LibraryProduct>();
                foreach (var entry in xmlEntries)
                {
                    var parsed = FromXml(ManufacturerImportSupport.ReadZipEntryText(entry), source, license);
                    if (parsed.IsSuccess)
                    {
                        all.AddRange(parsed.Value);
                    }
                }

                return all.Count == 0
                    ? Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.edz_bos", "EDZ arşivinde geçerli parça XML tanımı bulunamadı."))
                    : Result.Success<IReadOnlyList<LibraryProduct>>(all);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.okunamadi", $"'{path}' EDZ paketi okunamadı: {ex.Message}"));
            }
        }

        var text = ManufacturerImportSupport.ReadTextFile(path);
        return text.IsFailure ? Result.Failure<IReadOnlyList<LibraryProduct>>(text.Error!) : FromXml(text.Value, source, license);
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromXml(string xml, string source, string license)
    {
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        var loaded = ManufacturerImportSupport.LoadXml(xml, "EDZ part.xml");
        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(loaded.Error!);
        }

        var doc = loaded.Value;
        var partElements = doc.ElementsNamed("part").ToList();
        if (partElements.Count == 0 && doc.Root is not null)
        {
            partElements.Add(doc.Root);
        }

        var products = new List<LibraryProduct>();
        foreach (var part in partElements)
        {
            var orderCode = part.Attr("P_ARTICLE_ORDERNR", "ordernr", "orderCode", "partnr")
                ?? part.TextOf("P_ARTICLE_ORDERNR", "ordernr", "partnr");
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                continue;
            }

            var manufacturer = ManufacturerImportSupport.FallbackText(
                part.Attr("P_ARTICLE_MANUFACTURER", "manufacturer") ?? part.TextOf("P_ARTICLE_MANUFACTURER", "manufacturer"),
                source.Trim());
            var typeNr = part.Attr("P_ARTICLE_TYPENR", "typenr") ?? part.TextOf("P_ARTICLE_TYPENR", "typenr");
            var desc = part.Attr("P_ARTICLE_DESCR1", "description") ?? part.TextOf("P_ARTICLE_DESCR1", "description");

            var templates = new List<FunctionTemplate>();
            foreach (var ft in part.ElementsNamed("functiontemplate").Concat(part.ElementsNamed("FunctionTemplate")))
            {
                var defName = ft.Attr("functiondefname", "kind", "category") ?? ft.TextOf("functiondefname", "kind");
                var points = ft.ElementsNamed("connectionpoint")
                    .Select(cp => cp.Attr("designation", "name") ?? ManufacturerImportSupport.Clean(cp.Value))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .ToList();
                if (points.Count == 0 && (ft.Attr("connections", "pins") is { } rawPins))
                {
                    points.AddRange(rawPins.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }

                if (points.Count == 0)
                {
                    points.AddRange(["1", "2"]);
                }

                templates.Add(new FunctionTemplate(MapFunctionKind(defName), points, Description: ManufacturerImportSupport.Clean(defName)));
            }

            if (templates.Count == 0)
            {
                var etim = part.Attr("etim", "etimClass") ?? part.TextOf("etim", "etimClass");
                templates.AddRange(EtimTemplateMapper.TemplatesForClass(etim, desc ?? typeNr));
            }

            var props = new List<ProductProperty>();
            if (part.Attr("partnr") is { } partNr && !string.Equals(partNr, orderCode, StringComparison.Ordinal))
            {
                props.Add(new ProductProperty("PartNr", partNr));
            }

            products.Add(new LibraryProduct(
                new ProductDefinition(manufacturer, orderCode, templates, ManufacturerImportSupport.Clean(typeNr), ManufacturerImportSupport.Clean(desc)),
                source.Trim(),
                license.Trim(),
                "EDZ (Deneysel)",
                props));
        }

        return products.Count == 0
            ? Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.edz_bos", "EDZ XML içinde geçerli parça bulunamadı."))
            : Result.Success<IReadOnlyList<LibraryProduct>>(products);
    }

    private static FunctionKind MapFunctionKind(string? hint)
    {
        var text = hint?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Contains("coil") || text.Contains("bobin"))
        {
            return FunctionKind.Coil;
        }

        if (text.Contains("nc") || text.Contains("normal kapalı"))
        {
            return FunctionKind.NcContact;
        }

        if (text.Contains("power") && text.Contains("contact"))
        {
            return FunctionKind.PowerContact;
        }

        if (text.Contains("contact") || text.Contains("kontak") || text.Contains("no"))
        {
            return FunctionKind.NoContact;
        }

        if (text.Contains("terminal") || text.Contains("klemens"))
        {
            return FunctionKind.Terminal;
        }

        if (text.Contains("protect") || text.Contains("breaker") || text.Contains("sigorta"))
        {
            return FunctionKind.Protection;
        }

        if (text.Contains("motor"))
        {
            return FunctionKind.Motor;
        }

        if (text.Contains("plc"))
        {
            return FunctionKind.PlcChannel;
        }

        return FunctionKind.Generic;
    }
}

/// <summary>
/// SkyCAD parça/katalog XML veya paket dosyalarından ürün ve bağlantı uçları içe aktarır (ADR-0059).
/// </summary>
public static class SkyCadImporter
{
    public static Result<IReadOnlyList<LibraryProduct>> FromFile(string path, string source, string license)
    {
        var text = ManufacturerImportSupport.ReadTextFile(path);
        return text.IsFailure ? Result.Failure<IReadOnlyList<LibraryProduct>>(text.Error!) : FromXml(text.Value, source, license);
    }

    public static Result<IReadOnlyList<LibraryProduct>> FromXml(string xml, string source, string license)
    {
        if (ManufacturerImportSupport.CheckAttribution(source, license) is { } err)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(err);
        }

        var loaded = ManufacturerImportSupport.LoadXml(xml, "SkyCAD");
        if (loaded.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LibraryProduct>>(loaded.Error!);
        }

        var doc = loaded.Value;
        var parts = doc.ElementsNamed("CataloguePart")
            .Concat(doc.ElementsNamed("Component"))
            .Concat(doc.ElementsNamed("Part"))
            .ToList();
        if (parts.Count == 0 && doc.Root is not null)
        {
            parts.Add(doc.Root);
        }

        var products = new List<LibraryProduct>();
        foreach (var part in parts)
        {
            var orderCode = part.TextOf("PartNumber", "CatalogueNumber", "ArticleNumber")
                ?? part.Attr("PartNumber", "CatalogueNumber", "Name");
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                continue;
            }

            var manufacturer = ManufacturerImportSupport.FallbackText(
                part.TextOf("Manufacturer", "Brand") ?? part.Attr("Manufacturer"),
                source.Trim());
            var desc = part.TextOf("Description", "Title") ?? part.Attr("Description");
            var className = part.TextOf("ClassName", "Category") ?? part.Attr("ClassName");
            var rated = ManufacturerImportSupport.ParseDecimal(part.TextOf("RatedCurrent", "Current"));

            var pins = part.ElementsNamed("Terminal")
                .Concat(part.ElementsNamed("ConnectionPoint"))
                .Select(t => t.Attr("Name", "Number") ?? t.TextOf("Name", "Number"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            IReadOnlyList<FunctionTemplate> templates = pins.Count > 0
                ? [new FunctionTemplate(FunctionKind.Generic, pins, RatedCurrentA: rated, Description: ManufacturerImportSupport.Clean(className ?? desc))]
                : EtimTemplateMapper.TemplatesForClass(null, className ?? desc, rated);

            products.Add(new LibraryProduct(
                new ProductDefinition(manufacturer, orderCode, templates, ManufacturerImportSupport.Clean(className), ManufacturerImportSupport.Clean(desc)),
                source.Trim(),
                license.Trim(),
                ManufacturerImportSupport.FallbackText(className, "SkyCAD Kataloğu"),
                []));
        }

        return products.Count == 0
            ? Result.Failure<IReadOnlyList<LibraryProduct>>(new Error("kutuphane.skycad_bos", "SkyCAD dosyasında geçerli parça bulunamadı."))
            : Result.Success<IReadOnlyList<LibraryProduct>>(products);
    }
}
