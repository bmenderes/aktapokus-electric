using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Ai;

/// <summary>Doğrulanmış bir öneri: model bu patch'i üretti, şemaya uydu, projede uygulanabildi ve yeni ERC hatası çıkarmadı.</summary>
/// <param name="PatchJson">Kaynağı <c>Ai</c> olarak işaretlenmiş patch (<c>ake apply</c> ile uygulanabilir).</param>
/// <param name="Patch">Okunmuş patch.</param>
/// <param name="NewWarnings">Patch'in getirdiği yeni uyarılar (hata yok); kullanıcıya gösterilir.</param>
/// <param name="Attempts">Kaç denemede doğrulandı.</param>
public sealed record PatchProposal(string PatchJson, Patch Patch, IReadOnlyList<string> NewWarnings, int Attempts);

/// <summary>
/// İstemden patch önerisi üretir (ADR-0044): üret → oku → projenin kopyasında uygula → ERC → hata metnini modele geri ver → yeniden dene.
/// Gerçek projeye hiçbir şey yazmaz; öneriyi kullanıcı onaylayıp uygular.
/// </summary>
public sealed class PatchGenerator(
    IChatProvider provider,
    int maxAttempts = 3,
    IProductCatalog? catalog = null,
    ISymbolCatalog? symbols = null,
    KnowledgeBase? knowledge = null)
{
    // İlk istek kısa ve değişmez bir başlangıca sahiptir (sistem istemi + proje özeti + istek): Ollama ortak başlangıcı önbellekte tutar,
    // CPU'da dakikalar süren istem işlemesi tekrarlanmaz. Bilgi tabanı (örnek ve notlar) yalnızca doğrulama başarısız olursa yeniden deneme
    // mesajına eklenir; canlı ölçümde ilk istekte örnek vermek hem 4 kat yavaşlattı hem de bağlantı komutlarının atlanmasına yol açtı (ADR-0044).
    private static List<ChatMessage> BuildMessages(Project project, string request) =>
    [
        new("system", PatchPrompt.System),
        new("user", $"Proje özeti:\n{ProjectSummary.Build(project, request)}\nİstek: {request}"),
    ];

    // Reddedilen patch'in hatası, istemle ilgili doğrulanmış örnek ve notlarla birlikte modele geri verilir.
    private string Feedback(string request, Error error)
    {
        var bank = knowledge ?? KnowledgeBase.Default;
        var text = new System.Text.StringBuilder($"Bu patch reddedildi: {error.Message}\n");
        if (bank.RetrieveExamples(request, count: 1) is [var example])
        {
            text.AppendLine($"Doğru biçime örnek (başka bir projeden; tag'leri senin projenden almalısın): istek: {example.Request}\nPatch: {example.PatchJson}");
        }

        foreach (var note in bank.RetrieveSnippets(request, count: 2))
        {
            text.AppendLine($"Not — {note.Title}: {note.Text}");
        }

        text.Append("Hatayı düzelterek patch'i baştan yaz.");
        return text.ToString();
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<Result<PatchProposal>> ProposeAsync(Project project, string request, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        var baseline = ProjectJson.Serialize(project);
        var before = Findings(project);
        var messages = BuildMessages(project, request);

        Error? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var reply = await provider.CompleteAsync(new ChatRequest(messages, PatchPrompt.Schema), cancellation).ConfigureAwait(false);
            if (reply.IsFailure)
            {
                return Result.Failure<PatchProposal>(reply.Error!); // sunucu yoksa yeniden denemek anlamsız
            }

            var checkedReply = Check(reply.Value, baseline, before, attempt);
            if (checkedReply.IsSuccess)
            {
                return checkedReply;
            }

            last = checkedReply.Error!;
            messages.Add(new("assistant", reply.Value));
            messages.Add(new("user", Feedback(request, last)));
        }

        return Result.Failure<PatchProposal>(new Error(
            "ai.patch_dogrulanamadi", $"{maxAttempts} denemede geçerli patch üretilemedi. Son hata: {last?.Message}"));
    }

    private Result<PatchProposal> Check(string reply, string baseline, HashSet<string> before, int attempt)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(reply);
        }
        catch (JsonException ex)
        {
            return Result.Failure<PatchProposal>(new Error("ai.json_gecersiz", $"Yanıt JSON değil: {ex.Message}"));
        }

        if (node is not JsonObject root)
        {
            return Result.Failure<PatchProposal>(new Error("ai.json_gecersiz", "Yanıt bir JSON nesnesi değil."));
        }

        root["patchVersion"] = PatchJson.CurrentPatchVersion;
        root["source"] = "Ai";
        var json = root.ToJsonString(Pretty);

        var patch = PatchJson.Deserialize(json);
        if (patch.IsFailure)
        {
            return Result.Failure<PatchProposal>(patch.Error!);
        }

        if (patch.Value.CommandCount == 0)
        {
            return Result.Failure<PatchProposal>(new Error("ai.patch_bos", "Patch'te hiç komut yok."));
        }

        // Gerçek projeye dokunmadan, kopyasında dene.
        var copy = ProjectJson.Deserialize(baseline).Value;
        var applied = new CommandStack(copy).Execute(patch.Value.ToCommand(catalog, symbols), ChangeSource.Ai, "ai");
        if (applied.IsFailure)
        {
            return Result.Failure<PatchProposal>(applied.Error!);
        }

        var added = ErcEngine.Default.Run(copy).Findings.Where(finding => !before.Contains(Key(finding))).ToList();
        var errors = added.Where(finding => finding.Severity == Severity.Error).ToList();
        if (errors.Count > 0)
        {
            return Result.Failure<PatchProposal>(new Error(
                "ai.erc_hatasi", "Patch yeni ERC hatası getiriyor: " + string.Join("; ", errors.Select(error => error.Message))));
        }

        return Result.Success(new PatchProposal(json, patch.Value, [.. added.Select(finding => finding.Message)], attempt));
    }

    private static HashSet<string> Findings(Project project) => [.. ErcEngine.Default.Run(project).Findings.Select(Key)];

    private static string Key(ErcFinding finding) => $"{finding.RuleId}|{finding.Message}";
}
