using Ake.Ai;
using Ake.Core;
using Ake.Core.Commands;

namespace Ake.Editor;

/// <summary>Yerel LLM ile istem yazarak değişiklik: sağlayıcı/model seçimi, öneri ve onaylı uygulama, oturum tarafı (ADR-0044).</summary>
public sealed partial class EditorSession
{
    /// <summary>Seçili sağlayıcı ve model. Varsayılan yerel Ollama'dır; <see cref="AiSettingsPath"/> verilirse değişiklik orada saklanır.</summary>
    public AiSettings AiSettings { get; private set; } = AiSettings.Default;

    /// <summary>Ayarın saklandığı dosya (kullanıcı profili). Boşsa ayar yalnızca oturum boyunca tutulur.</summary>
    public string? AiSettingsPath { get; private set; }

    /// <summary>Testler sahte sağlayıcı verir; boşsa <see cref="AiSettings"/>'e göre gerçek sağlayıcı kurulur.</summary>
    public Func<IChatProvider>? ChatProviderFactory { get; set; }

    /// <summary>Ayar dosyasını okur ve sonraki değişikliklerin yazılacağı yeri belirler.</summary>
    public void UseAiSettingsFile(string path)
    {
        AiSettingsPath = path;
        AiSettings = AiSettings.Load(path);
    }

    /// <summary>Sağlayıcı/modeli değiştirir ve (dosya verilmişse) saklar. Geçersiz ayar reddedilir.</summary>
    public Result SetAiSettings(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Check() is { } error)
        {
            return Result.Failure(error);
        }

        AiSettings = settings;
        return AiSettingsPath is null ? Result.Success() : settings.Save(AiSettingsPath);
    }

    /// <summary>
    /// Yerel Ollama modelini önceden belleğe yükler: CPU'da soğuk yükleme dakikalar sürer, pencere açılırken başlatılırsa ilk istek beklemez.
    /// Uzak sağlayıcıda ya da sahte sağlayıcıyla bir şey yapmaz; hata önemsizdir.
    /// </summary>
    public Task PreloadAiModelAsync(CancellationToken cancellation = default) =>
        ChatProviderFactory is null && AiSettings.Provider == AiSettings.Ollama
            ? new OllamaChatProvider(AiSettings.Model, AiSettings.Address).PreloadAsync(cancellation)
            : Task.CompletedTask;

    /// <summary>Seçili sağlayıcıdaki modeller (seçici için).</summary>
    public Task<Result<IReadOnlyList<string>>> ListAiModelsAsync(AiSettings settings, CancellationToken cancellation = default) =>
        ChatProviders.ListModelsAsync(settings, cancellation);

    /// <summary>
    /// İstemden doğrulanmış bir patch önerisi üretir. <paramref name="settings"/> verilirse önce o ayar seçilir ve saklanır. Projeye hiçbir
    /// şey yazılmaz ve geri alma listesine girmez; öneriyi kullanıcı onaylarsa <see cref="ApplyAiProposal"/> uygular.
    /// </summary>
    public async Task<Result<PatchProposal>> ProposeAiChangeAsync(string request, AiSettings? settings = null, CancellationToken cancellation = default)
    {
        if (Project is null)
        {
            return Result.Failure<PatchProposal>(new Error("editor.proje_yok", "Proje açık değil."));
        }

        if (settings is not null && SetAiSettings(settings) is { IsFailure: true } rejected)
        {
            return Result.Failure<PatchProposal>(rejected.Error!);
        }

        var provider = ChatProviderFactory?.Invoke() ?? ChatProviders.Create(AiSettings);
        var generator = new PatchGenerator(provider, maxAttempts: 3, _library, _library);
        return await generator.ProposeAsync(Project, request, cancellation).ConfigureAwait(false);
    }

    /// <summary>Onaylanan öneriyi tek adımda uygular (kaynak: yapay zeka, denetim kaydına yazılır); tek Geri Al ile geri alınır.</summary>
    public Result ApplyAiProposal(PatchProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return _stack is null
            ? NoProject()
            : Refreshed(_stack.Execute(proposal.Patch.ToCommand(_library, _library), ChangeSource.Ai, "yapay zeka"));
    }
}
