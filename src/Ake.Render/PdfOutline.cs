using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ake.Render;

/// <summary>
/// SkiaSharp'ın yazdığı PDF'e yer imi (outline) ekler. SkiaSharp yer imi yazmaz; dosyanın sonuna PDF'in
/// artımlı güncellemesi (incremental update) eklenir: yer imi nesneleri, <c>/Outlines</c> ile yeniden
/// yazılan katalog ve önceki tabloya <c>/Prev</c> ile bağlı yeni xref tablosu. Önceki baytlara dokunulmaz.
/// </summary>
internal static partial class PdfOutline
{
    [GeneratedRegex(@"startxref\s+(\d+)\s+%%EOF\s*$")]
    private static partial Regex StartXref();

    [GeneratedRegex(@"(\d+) 0 R")]
    private static partial Regex Reference();

    /// <param name="entries">Yer imi başlığı ve hedef sayfanın sıfırdan başlayan sırası.</param>
    public static void Append(string path, IReadOnlyList<(string Title, int Page)> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var original = File.ReadAllBytes(path);
        var text = Encoding.Latin1.GetString(original);

        var startXref = StartXref().Match(text);
        if (!startXref.Success)
        {
            throw new InvalidOperationException("PDF'in sonunda startxref bulunamadı.");
        }

        var xrefAt = int.Parse(startXref.Groups[1].Value, CultureInfo.InvariantCulture);
        var (offsets, trailer) = ReadXref(text, xrefAt);
        var size = int.Parse(Value(trailer, "/Size"), CultureInfo.InvariantCulture);
        var root = int.Parse(Value(trailer, "/Root"), CultureInfo.InvariantCulture);
        var catalog = Body(text, offsets, root);
        if (catalog.Contains("/Outlines", StringComparison.Ordinal) || !catalog.EndsWith(">>", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PDF kataloğu beklenen biçimde değil ya da zaten yer imi var.");
        }

        var pages = new List<int>();
        CollectPages(text, offsets, int.Parse(Value(catalog, "/Pages"), CultureInfo.InvariantCulture), pages);

        var outlines = size;
        var first = size + 1;
        var last = size + entries.Count;
        var objects = new List<(int Number, string Body)>
        {
            (root, catalog[..^2] + $"\n/Outlines {outlines} 0 R\n/PageMode /UseOutlines>>"),
            (outlines, $"<</Type /Outlines\n/First {first} 0 R\n/Last {last} 0 R\n/Count {entries.Count}>>"),
        };
        for (var i = 0; i < entries.Count; i++)
        {
            var (title, page) = entries[i];
            if (page < 0 || page >= pages.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), $"Yer imi '{title}' olmayan bir sayfayı gösteriyor ({page}).");
            }

            var number = first + i;
            var item = new StringBuilder();
            item.Append(CultureInfo.InvariantCulture, $"<</Title {HexText(title)}\n/Parent {outlines} 0 R");
            if (number > first)
            {
                item.Append(CultureInfo.InvariantCulture, $"\n/Prev {number - 1} 0 R");
            }

            if (number < last)
            {
                item.Append(CultureInfo.InvariantCulture, $"\n/Next {number + 1} 0 R");
            }

            item.Append(CultureInfo.InvariantCulture, $"\n/Dest [{pages[page]} 0 R /Fit]>>");
            objects.Add((number, item.ToString()));
        }

        var update = new StringBuilder();
        var position = original.Length;
        if (original[^1] != (byte)'\n')
        {
            update.Append('\n');
        }

        var written = new Dictionary<int, int>();
        foreach (var (number, body) in objects)
        {
            written[number] = position + update.Length;
            update.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n");
        }

        // Her xref satırı tam 20 bayttır: 10 hane konum, boşluk, 5 hane nesil, boşluk, "n", CR LF.
        var newXref = position + update.Length;
        update.Append("xref\n");
        update.Append(CultureInfo.InvariantCulture, $"{root} 1\n");
        update.Append(CultureInfo.InvariantCulture, $"{written[root]:D10} 00000 n\r\n");
        update.Append(CultureInfo.InvariantCulture, $"{outlines} {entries.Count + 1}\n");
        for (var number = outlines; number <= last; number++)
        {
            update.Append(CultureInfo.InvariantCulture, $"{written[number]:D10} 00000 n\r\n");
        }

        update.Append(CultureInfo.InvariantCulture, $"trailer\n<</Size {last + 1}\n/Root {root} 0 R");
        foreach (var key in new[] { "/Info", "/ID" })
        {
            if (Entry(trailer, key) is { } entry)
            {
                update.Append('\n').Append(entry);
            }
        }

        update.Append(CultureInfo.InvariantCulture, $"\n/Prev {xrefAt}>>\nstartxref\n{newXref}\n%%EOF\n");

        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        stream.Write(Encoding.ASCII.GetBytes(update.ToString()));
    }

    // Klasik xref tablosu: "xref", alt bölümler ("ilk adet" ve 20 baytlık satırlar), sonra "trailer".
    private static (Dictionary<int, int> Offsets, string Trailer) ReadXref(string text, int at)
    {
        if (string.CompareOrdinal(text, at, "xref", 0, 4) != 0)
        {
            throw new InvalidOperationException("PDF'te klasik xref tablosu yok.");
        }

        var trailerAt = text.IndexOf("trailer", at, StringComparison.Ordinal);
        var offsets = new Dictionary<int, int>();
        var lines = text[(at + 4)..trailerAt].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var i = 0;
        while (i < lines.Length)
        {
            var header = lines[i].Split(' ');
            var start = int.Parse(header[0], CultureInfo.InvariantCulture);
            var count = int.Parse(header[1], CultureInfo.InvariantCulture);
            for (var k = 0; k < count; k++)
            {
                var entry = lines[i + 1 + k].Split(' ');
                if (entry[2] == "n")
                {
                    offsets[start + k] = int.Parse(entry[0], CultureInfo.InvariantCulture);
                }
            }

            i += count + 1;
        }

        var end = text.IndexOf("startxref", trailerAt, StringComparison.Ordinal);
        return (offsets, text[(trailerAt + 7)..end].Trim());
    }

    private static string Body(string text, Dictionary<int, int> offsets, int number)
    {
        if (!offsets.TryGetValue(number, out var at))
        {
            throw new InvalidOperationException($"PDF nesnesi {number} xref tablosunda yok.");
        }

        var start = text.IndexOf("obj", at, StringComparison.Ordinal) + 3;
        var end = text.IndexOf("endobj", start, StringComparison.Ordinal);
        return text[start..end].Trim();
    }

    private static void CollectPages(string text, Dictionary<int, int> offsets, int number, List<int> pages)
    {
        var body = Body(text, offsets, number);
        if (!body.Contains("/Type /Pages", StringComparison.Ordinal))
        {
            pages.Add(number);
            return;
        }

        var kidsAt = body.IndexOf("/Kids", StringComparison.Ordinal);
        var open = body.IndexOf('[', kidsAt);
        var close = body.IndexOf(']', open);
        foreach (Match kid in Reference().Matches(body[open..close]))
        {
            CollectPages(text, offsets, int.Parse(kid.Groups[1].Value, CultureInfo.InvariantCulture), pages);
        }
    }

    // "/Root 18 0 R" → "18"; "/Size 23" → "23".
    private static string Value(string dictionary, string key)
    {
        var match = Regex.Match(dictionary, Regex.Escape(key) + @"\s+(\d+)");
        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"PDF sözlüğünde {key} yok.");
    }

    // Trailer'daki bir girdinin tamamı (anahtar ve değeri): "/Info 6 0 R" ya da "/ID [<..> <..>]".
    private static string? Entry(string dictionary, string key)
    {
        var match = Regex.Match(dictionary, Regex.Escape(key) + @"\s+(\d+ \d+ R|\[[^\]]*\])");
        return match.Success ? match.Value : null;
    }

    // Türkçe harfler için UTF-16BE, BOM ile, onaltılık yazı: <FEFF...>.
    private static string HexText(string value)
    {
        var builder = new StringBuilder("<FEFF");
        foreach (var b in Encoding.BigEndianUnicode.GetBytes(value))
        {
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.Append('>').ToString();
    }
}
