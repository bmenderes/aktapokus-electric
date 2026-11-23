using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ake.Ai;

/// <summary>Kısa bir kural/biçim notu (RAG parçası).</summary>
public sealed record KnowledgeSnippet(string Title, string Keywords, string Text);

/// <summary>Doğrulanmış bir istem → patch örneği (few-shot). Testler her örneğin örnek projede uygulandığını ve yeni ERC hatası çıkarmadığını denetler.</summary>
public sealed record KnowledgeExample(string Id, string Request, string PatchJson);

/// <summary>
/// Modelin ağırlıklarına dokunmadan onu yönlendiren bilgi tabanı (ADR-0044): komut ve tag kuralları, standart notları ve doğrulanmış örnekler.
/// İstemle ilgili olanlar anahtar sözcük örtüşmesiyle seçilir ve istemin içine konur (RAG + few-shot). Gömme modeli gerekmez; küçük ve hızlıdır.
/// </summary>
public sealed class KnowledgeBase
{
    private const string Resource = "Ake.Ai.Knowledge.bilgi.json";

    private KnowledgeBase(IReadOnlyList<KnowledgeSnippet> snippets, IReadOnlyList<KnowledgeExample> examples)
    {
        Snippets = snippets;
        Examples = examples;
    }

    /// <summary>Bilgi tabanı olmadan (örnek ve not eklenmez); RAG'ın katkısını ölçmek için.</summary>
    public static KnowledgeBase Empty { get; } = new([], []);

    /// <summary>Derlemeye gömülü varsayılan bilgi tabanı.</summary>
    public static KnowledgeBase Default { get; } = LoadEmbedded();

    public IReadOnlyList<KnowledgeSnippet> Snippets { get; }

    public IReadOnlyList<KnowledgeExample> Examples { get; }

    /// <summary>İstemle en çok örtüşen <paramref name="count"/> not (örtüşmesi olmayanlar gelmez).</summary>
    public IReadOnlyList<KnowledgeSnippet> RetrieveSnippets(string request, int count = 3) =>
        Rank(request, Snippets, snippet => $"{snippet.Title} {snippet.Keywords}", count);

    /// <summary>İstemle en çok örtüşen <paramref name="count"/> doğrulanmış örnek.</summary>
    public IReadOnlyList<KnowledgeExample> RetrieveExamples(string request, int count = 3) =>
        Rank(request, Examples, example => $"{example.Request} {example.Id.Replace('-', ' ')}", count);

    private static List<T> Rank<T>(string request, IReadOnlyList<T> items, Func<T, string> text, int count)
    {
        var wanted = Tokens(request);
        return [.. items
            .Select((item, index) => (item, index, score: Tokens(text(item)).Count(wanted.Contains)))
            .Where(entry => entry.score > 0)
            .OrderByDescending(entry => entry.score)
            .ThenBy(entry => entry.index)
            .Take(count)
            .Select(entry => entry.item)];
    }

    /// <summary>Küçük harf, Türkçe harfler sadeleştirilmiş, ilk 5 karakter (kaba kök): "bağlantıyı" ve "bağla" aynı köke düşer.</summary>
    internal static HashSet<string> Tokens(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var ch in text.ToLower(CultureInfo.InvariantCulture))
        {
            folded.Append(ch switch { 'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'İ' => 'i', _ => ch });
        }

        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        folded.Append(' ');
        foreach (var ch in folded.ToString())
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
                continue;
            }

            if (current.Length >= 3)
            {
                tokens.Add(current.Length > 5 ? current.ToString(0, 5) : current.ToString());
            }

            current.Clear();
        }

        return tokens;
    }

    private static KnowledgeBase LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Gömülü bilgi tabanı yok: {Resource}");
        var root = JsonNode.Parse(stream)!.AsObject();
        var snippets = root["snippets"]!.AsArray()
            .Select(node => new KnowledgeSnippet(node!["title"]!.GetValue<string>(), node["keywords"]!.GetValue<string>(), node["text"]!.GetValue<string>()))
            .ToList();
        var examples = root["examples"]!.AsArray()
            .Select(node => new KnowledgeExample(
                node!["id"]!.GetValue<string>(),
                node["request"]!.GetValue<string>(),
                node["patch"]!.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })))
            .ToList();
        return new KnowledgeBase(snippets, examples);
    }
}
