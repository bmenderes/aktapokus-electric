using System.Diagnostics;
using Xunit.Abstractions;

namespace Ake.Ai.Tests;

/// <summary>
/// Gerçek yerel model (Ollama) ile değerlendirme seti (examples/ai/degerlendirme.json; ADR-0044). Yavaştır (CPU'da istem başına dakikalar) ve
/// Ollama gerektirir; yalnızca <c>AKE_LIVE_LLM=1</c> ortam değişkeniyle çalışır, aksi halde hemen döner. Model <c>AKE_LLM_MODEL</c> ile değişir;
/// <c>AKE_LLM_PLAIN=1</c> bilgi tabanını (örnekler ve notlar) kapatır ve RAG'ın katkısını ölçmek için kullanılır.
/// Çalıştırma: <c>$env:AKE_LIVE_LLM='1'; dotnet test tests/Ake.Ai.Tests --filter Category=Live</c>
/// </summary>
[Trait("Category", "Live")]
public class LiveModelTests(ITestOutputHelper log)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("AKE_LIVE_LLM") == "1";

    public static TheoryData<string> Cases => EvaluationCase.Ids();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_local_model_solves_the_evaluation_case(string id)
    {
        if (!Enabled)
        {
            return; // gerçek model testi yalnızca AKE_LIVE_LLM=1 ile çalışır
        }

        var item = EvaluationCase.Load().Single(candidate => candidate.Id == id);
        var model = Environment.GetEnvironmentVariable("AKE_LLM_MODEL") ?? OllamaChatProvider.DefaultModel;
        var plain = Environment.GetEnvironmentVariable("AKE_LLM_PLAIN") == "1";
        var generator = new PatchGenerator(new OllamaChatProvider(model), maxAttempts: 3, knowledge: plain ? KnowledgeBase.Empty : null);
        var clock = Stopwatch.StartNew();

        var result = await generator.ProposeAsync(EvaluationCase.RefMachine(), item.Request);

        log.WriteLine($"VAKA {item.Id} | model {model}{(plain ? " (bilgi tabanı kapalı)" : string.Empty)} | {clock.Elapsed.TotalSeconds:F0} sn | "
            + (result.IsSuccess ? $"{result.Value.Attempts}. denemede doğrulandı" : $"BAŞARISIZ: {result.Error!.Message}"));
        Assert.True(result.IsSuccess, result.Error?.Message);
        log.WriteLine(result.Value.PatchJson);

        // Doğrulanan öneri beklentileri de sağlamalı (yalnızca geçerli olmak yetmez).
        var after = EvaluationCase.RefMachine();
        Assert.True(EvaluationCase.Apply(after, result.Value.PatchJson).IsSuccess);
        var problems = item.Check(EvaluationCase.RefMachine(), after);
        log.WriteLine(problems.Count == 0 ? "SONUÇ: GEÇTİ" : "SONUÇ: BEKLENTİ TUTMADI → " + string.Join("; ", problems));
        Assert.Empty(problems);
    }
}
