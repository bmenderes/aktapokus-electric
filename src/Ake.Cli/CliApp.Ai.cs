using System.CommandLine;
using System.Text;
using Ake.Ai;
using Ake.Core;
using Ake.Library;
using Ake.Serialization;

namespace Ake.Cli;

internal static partial class CliApp
{
    // Yerel LLM ile istemden patch önerisi (ADR-0044). Proje değişmez; öneri doğrulanmış patch olarak yazılır, uygulamak ayrı ve onaylı bir adımdır (ake apply).
    private static Command AiCommand(TextWriter output, TextWriter error, string? defaultLibraryPath)
    {
        var project = new Argument<string>("proje") { Description = "Proje dosyası (.json)" };
        var request = new Argument<string>("istem") { Description = "Yapılacak değişiklik, örn. \"K7 kontaktörünü ekle ve bobinini T1'e bağla\"" };
        var outPath = new Option<string?>("--out") { Description = "Önerilen patch'i bu dosyaya yaz (verilmezse ekrana)" };
        var provider = new Option<string>("--provider")
        {
            Description = "ollama (yerel, varsayılan), claude (Anthropic), antigravity (Gemini) ya da openai",
            DefaultValueFactory = _ => AiSettings.Ollama,
        };
        var model = new Option<string>("--model")
        {
            Description = "Ollama model adı",
            DefaultValueFactory = _ => OllamaChatProvider.DefaultModel,
        };
        var address = new Option<string>("--address")
        {
            Description = "Ollama adresi (yerel olmalı)",
            DefaultValueFactory = _ => OllamaChatProvider.DefaultAddress,
        };
        var attempts = new Option<int>("--attempts")
        {
            Description = "Doğrulama başarısız olursa en çok kaç deneme",
            DefaultValueFactory = _ => 3,
        };
        var library = new Option<string?>("--library") { Description = "Komponent kütüphanesi dosyası (patch kütüphane ürünü kullanıyorsa)" };

        var command = new Command("ai", "Yerel LLM ile istemden doğrulanmış bir patch önerir (projeyi değiştirmez)");
        command.Arguments.Add(project);
        command.Arguments.Add(request);
        command.Options.Add(outPath);
        command.Options.Add(provider);
        command.Options.Add(model);
        command.Options.Add(address);
        command.Options.Add(attempts);
        command.Options.Add(library);
        command.SetAction(parse =>
        {
            var loaded = ProjectFile.Load(parse.GetValue(project)!);
            if (loaded.IsFailure)
            {
                return Fail(error, loaded.Error!);
            }

            var libraryPath = parse.GetValue(library)
                ?? (defaultLibraryPath is not null && File.Exists(defaultLibraryPath) ? defaultLibraryPath : null);
            var opened = libraryPath is null ? null : ComponentLibrary.Open(libraryPath);
            if (opened is { IsFailure: true })
            {
                return Fail(error, opened.Error!);
            }

            using var catalog = opened?.Value;
            var settings = new AiSettings(parse.GetValue(provider)!, parse.GetValue(address)!, parse.GetValue(model)!);
            if (settings.Check() is { } invalid)
            {
                return Fail(error, invalid);
            }

            if (!settings.IsLocal)
            {
                output.WriteLine($"UYARI: {settings.Address} bu bilgisayar değil; proje özeti oraya gönderilir.");
            }

            var generator = new PatchGenerator(ChatProviders.Create(settings), Math.Max(1, parse.GetValue(attempts)), catalog, catalog);
            output.WriteLine("Öneri hazırlanıyor (CPU'da birkaç dakika sürebilir)...");
            var proposal = generator.ProposeAsync(loaded.Value, parse.GetValue(request)!).GetAwaiter().GetResult();
            if (proposal.IsFailure)
            {
                return Fail(error, proposal.Error!);
            }

            output.WriteLine($"Öneri doğrulandı ({proposal.Value.Attempts}. denemede): {proposal.Value.Patch.CommandCount} komut, yeni uyarı: {proposal.Value.NewWarnings.Count}.");
            foreach (var warning in proposal.Value.NewWarnings)
            {
                output.WriteLine($"  uyarı: {warning}");
            }

            if (parse.GetValue(outPath) is { } target)
            {
                File.WriteAllText(target, proposal.Value.PatchJson, new UTF8Encoding(false));
                output.WriteLine($"Patch yazıldı: {target}. İnceleyip uygulamak için: ake apply <proje> {target}");
            }
            else
            {
                output.WriteLine(proposal.Value.PatchJson);
            }

            return ExitOk;
        });
        return command;
    }
}
