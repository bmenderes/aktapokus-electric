namespace Ake.Cli.Tests;

/// <summary>
/// <c>ake ai</c> yerel Ollama'ya bağlanır; testte sunucu yoktur. Komutun bağlı olduğunu ve sunucu yokken
/// projeyi değiştirmeden, anlaşılır bir hatayla ve sıfır olmayan çıkış koduyla döndüğünü doğrular.
/// </summary>
public class AiCommandTests
{
    [Fact]
    public void Without_a_server_the_command_fails_clearly_and_leaves_the_project_untouched()
    {
        var directory = Directory.CreateTempSubdirectory("ake-ai");
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
            {
                root = root.Parent;
            }

            var project = Path.Combine(directory.FullName, "makine.json");
            File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), project);
            var before = File.ReadAllText(project);
            using var output = new StringWriter();
            using var error = new StringWriter();

            // 127.0.0.1:1 hiçbir şeyin dinlemediği bir adrestir.
            var exit = CliApp.Run(["ai", project, "K7 ekle", "--address", "http://127.0.0.1:1", "--attempts", "1"], output, error);

            Assert.NotEqual(0, exit);
            Assert.Contains("ai.sunucuya_ulasilamadi", error.ToString(), StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllText(project));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void An_unknown_provider_or_a_bad_address_is_rejected_before_any_request()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var project = Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json");
        foreach (var (arguments, code) in new[]
        {
            (new[] { "--provider", "uydurma" }, "ai.saglayici_gecersiz"),
            (["--address", "adres degil"], "ai.adres_gecersiz"),
        })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = CliApp.Run(["ai", project, "x", .. arguments], output, error);

            Assert.NotEqual(0, exit);
            Assert.Contains(code, error.ToString(), StringComparison.Ordinal);
        }
    }
}
