using Ake.Core;
using Ake.Core.Erc;
using Ake.Serialization;

namespace Ake.Ai.Tests;

/// <summary>Bilgi tabanı (RAG + few-shot), alakalı proje özeti ve değerlendirme setinin kendisi (ADR-0044); gerçek model gerekmez.</summary>
public class KnowledgeTests
{
    [Fact]
    public void Every_example_patch_applies_to_the_reference_project_without_new_erc_errors()
    {
        Assert.NotEmpty(KnowledgeBase.Default.Examples);
        foreach (var example in KnowledgeBase.Default.Examples)
        {
            var before = EvaluationCase.RefMachine();
            var after = EvaluationCase.RefMachine();

            var applied = EvaluationCase.Apply(after, example.PatchJson);

            Assert.True(applied.IsSuccess, $"{example.Id}: {applied.Error?.Message}");
            var known = ErcEngine.Default.Run(before).Findings.Select(finding => $"{finding.RuleId}|{finding.Message}").ToHashSet();
            var added = ErcEngine.Default.Run(after).Findings
                .Where(finding => finding.Severity == Severity.Error && !known.Contains($"{finding.RuleId}|{finding.Message}"))
                .ToList();
            Assert.True(added.Count == 0, $"{example.Id}: yeni ERC hatası {string.Join("; ", added.Select(finding => finding.Message))}");
        }
    }

    [Fact]
    public void Every_evaluation_reference_solution_satisfies_its_own_expectations()
    {
        foreach (var item in EvaluationCase.Load())
        {
            var before = EvaluationCase.RefMachine();
            var after = EvaluationCase.RefMachine();

            var applied = EvaluationCase.Apply(after, item.SolutionJson);

            Assert.True(applied.IsSuccess, $"{item.Id}: {applied.Error?.Message}");
            Assert.True(item.Check(before, after).Count == 0, $"{item.Id}: {string.Join("; ", item.Check(before, after))}");
        }
    }

    [Fact]
    public void Evaluation_requests_never_repeat_a_few_shot_example_so_the_score_is_not_inflated()
    {
        var examples = KnowledgeBase.Default.Examples.Select(example => example.Request).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(EvaluationCase.Load(), item => Assert.DoesNotContain(item.Request, examples));
    }

    [Theory]
    [InlineData("bobin-ekle", "ekle-bobin")]
    [InlineData("baglanti-sil", "baglantiyi-sil")]
    [InlineData("etiket-degistir", "etiket-degistir")]
    [InlineData("tel-numarasi", "tel-numarasi")]
    [InlineData("sigorta-ekle", "ekle-sigorta")]
    [InlineData("sayfa-ekle", "sayfa-ekle")]
    [InlineData("cihaz-kaldir", "cihazi-sil")]
    public void The_right_example_is_retrieved_for_each_kind_of_request(string caseId, string exampleId)
    {
        var request = EvaluationCase.Load().Single(item => item.Id == caseId).Request;

        Assert.Contains(exampleId, KnowledgeBase.Default.RetrieveExamples(request).Select(example => example.Id));
    }

    [Fact]
    public void Retrieval_folds_turkish_letters_and_stems_and_returns_nothing_for_unrelated_text()
    {
        Assert.Equal(KnowledgeBase.Tokens("bağlantıyı"), KnowledgeBase.Tokens("BAĞLANTI"));
        Assert.Contains("Kablo renkleri (IEC 60204-1)", KnowledgeBase.Default.RetrieveSnippets("PE kablosu hangi renk olmalı").Select(snippet => snippet.Title));
        Assert.Empty(KnowledgeBase.Default.RetrieveExamples("xyzqw abcde"));
    }

    [Fact]
    public void The_summary_lists_structure_nodes_and_all_devices_of_a_small_project()
    {
        var project = EvaluationCase.RefMachine();

        var summary = ProjectSummary.Build(project, "K7 ekle");

        Assert.Contains("Fonksiyon düğümleri: =G1, =M1, =M2, =C1, =E1", summary, StringComparison.Ordinal);
        Assert.Contains("Yer düğümleri: +P1, +S1", summary, StringComparison.Ordinal);
        Assert.Contains("=E1+P1-S0", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("istekle ilgili", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_large_project_is_cut_down_to_the_devices_the_request_names_and_their_neighbours()
    {
        var project = EvaluationCase.RefMachine();

        var summary = ProjectSummary.Build(project, "=M1+P1-K1 cihazının etiketini değiştir", fullLimit: 5);

        Assert.Contains("istekle ilgili", summary, StringComparison.Ordinal);
        Assert.Contains("=M1+P1-K1 ", summary, StringComparison.Ordinal);
        Assert.Contains("=M1+P1-F1 ", summary, StringComparison.Ordinal); // K1'e bağlı komşu
        Assert.DoesNotContain("=E1+P1-S0 ", summary, StringComparison.Ordinal);
        Assert.True(summary.Length < ProjectSummary.Build(project, "x").Length);
    }

    [Fact]
    public async Task The_first_request_is_short_and_plain_and_only_a_rejection_brings_in_the_relevant_example_and_notes()
    {
        var provider = new RecordingProvider("bu bir yanıt değil");

        await new PatchGenerator(provider, maxAttempts: 2).ProposeAsync(
            EvaluationCase.RefMachine(), "=M1+P1-K1 cihazının etiketini K9 olarak değiştir");

        var first = provider.Calls[0];
        Assert.Equal(["system", "user"], first.Select(message => message.Role));
        Assert.DoesNotContain("Örnek", first[^1].Content, StringComparison.Ordinal);
        Assert.StartsWith("Proje özeti:", first[^1].Content, StringComparison.Ordinal);
        Assert.EndsWith("K9 olarak değiştir", first[^1].Content, StringComparison.Ordinal);

        var retry = provider.Calls[1];
        Assert.Equal(["system", "user", "assistant", "user"], retry.Select(message => message.Role));
        Assert.Contains("reddedildi", retry[^1].Content, StringComparison.Ordinal);
        Assert.Contains("\"op\":\"retag\"", retry[^1].Content, StringComparison.Ordinal);
        Assert.Contains("Not —", retry[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_knowledge_base_the_rejection_carries_only_the_error()
    {
        var provider = new RecordingProvider("bozuk");

        await new PatchGenerator(provider, maxAttempts: 2, knowledge: KnowledgeBase.Empty).ProposeAsync(
            EvaluationCase.RefMachine(), "=M1+P1-K1 etiketini K9 yap");

        Assert.DoesNotContain("Not —", provider.Calls[1][^1].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Patch:", provider.Calls[1][^1].Content, StringComparison.Ordinal);
    }

    private sealed class RecordingProvider(string reply) : IChatProvider
    {
        public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

        public Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
        {
            Calls.Add([.. request.Messages]);
            return Task.FromResult(Result.Success(reply));
        }
    }
}
