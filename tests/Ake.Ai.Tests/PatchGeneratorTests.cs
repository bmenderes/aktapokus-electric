using System.Net;
using System.Text;
using Ake.Core;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Ai.Tests;

/// <summary>Gerçek model olmadan: sahte sağlayıcıyla üret → doğrula → geri besle döngüsü (ADR-0044).</summary>
public class PatchGeneratorTests
{
    private const string Valid = """
        { "description": "K7 ekle", "commands": [
          { "op": "addDevice", "tag": "=M1+P1-K7", "kind": "Generic", "functions": [ { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] },
          { "op": "connect", "from": "=M1+P1-K7:A2", "to": "-T1:-" }
        ] }
        """;

    private const string UnknownTarget = """
        { "commands": [
          { "op": "addDevice", "tag": "=M1+P1-K7", "functions": [ { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] },
          { "op": "connect", "from": "-K7:A1", "to": "-YOK:1" }
        ] }
        """;

    private static Project RefMachine()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        var loaded = ProjectFile.Load(Path.Combine(directory!.FullName, "examples", "ref-machine", "ref-machine.json"));
        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        return loaded.Value;
    }

    [Fact]
    public async Task A_valid_reply_becomes_a_proposal_marked_as_ai_without_touching_the_project()
    {
        var project = RefMachine();
        var before = ProjectJson.Serialize(project);
        var provider = new ScriptedProvider(Valid);

        var result = await new PatchGenerator(provider).ProposeAsync(project, "K7 kontaktörünü ekle");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value.Attempts);
        Assert.Equal(Ake.Core.Commands.ChangeSource.Ai, result.Value.Patch.Source);
        Assert.Equal(2, result.Value.Patch.CommandCount);
        Assert.Equal(before, ProjectJson.Serialize(project));
        Assert.Contains("PlcChannel", provider.Requests[0].JsonSchema!, StringComparison.Ordinal);
        Assert.Contains("K7 kontaktörünü ekle", provider.Requests[0].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("=M1+P1-K1", provider.Requests[0].Messages[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_patch_that_does_not_apply_is_sent_back_with_the_error_and_retried()
    {
        var provider = new ScriptedProvider(UnknownTarget, Valid);

        var result = await new PatchGenerator(provider).ProposeAsync(RefMachine(), "K7 ekle");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.Attempts);
        var feedback = provider.Requests[1].Messages;
        Assert.Equal("assistant", feedback[^2].Role);
        Assert.Contains("reddedildi", feedback[^1].Content, StringComparison.Ordinal);
        Assert.Contains("-YOK", feedback[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_json_and_empty_patches_are_rejected_and_give_up_after_the_attempt_limit()
    {
        var provider = new ScriptedProvider("bu bir yanıt", """{ "commands": [] }""", "tekrar yanıt");

        var result = await new PatchGenerator(provider, maxAttempts: 3).ProposeAsync(RefMachine(), "bir şey yap");

        Assert.Equal("ai.patch_dogrulanamadi", result.Error!.Code);
        Assert.Equal(3, provider.Requests.Count);
        Assert.Contains("tekrar", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_patch_that_adds_an_erc_error_is_rejected()
    {
        // Aynı noktayı iki farklı kontaktör bobinine… yerine geçersiz bir cihaz tag'i: tekrar eden tag ERC/komut tarafından reddedilir.
        const string duplicate = """
            { "commands": [ { "op": "addDevice", "tag": "=M1+P1-K1", "functions": [ { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] } ] }
            """;
        var provider = new ScriptedProvider(duplicate, duplicate, duplicate);

        var result = await new PatchGenerator(provider).ProposeAsync(RefMachine(), "K1 ekle");

        Assert.Equal("ai.patch_dogrulanamadi", result.Error!.Code);
    }

    [Fact]
    public async Task A_provider_failure_is_returned_at_once_without_retrying()
    {
        var provider = new ScriptedProvider();

        var result = await new PatchGenerator(provider).ProposeAsync(RefMachine(), "bir şey");

        Assert.Equal("ai.sunucuya_ulasilamadi", result.Error!.Code);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task The_summary_names_devices_by_tag_and_lists_their_points()
    {
        var summary = ProjectSummary.Build(RefMachine());

        Assert.Contains("=M1+P1-K1", summary, StringComparison.Ordinal);
        Assert.Contains("Bağlantılar:", summary, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_ollama_client_posts_the_chat_with_the_schema_and_reads_the_reply()
    {
        var handler = new RecordingHandler("""{ "message": { "role": "assistant", "content": "{\"commands\":[]}" } }""");
        var provider = new OllamaChatProvider("test-model", client: new HttpClient(handler) { BaseAddress = new Uri("http://yerel.test") });

        var result = await provider.CompleteAsync(new ChatRequest([new("user", "merhaba")], """{ "type": "object" }"""));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("{\"commands\":[]}", result.Value);
        Assert.Equal("/api/chat", handler.Path);
        Assert.Contains("\"model\":\"test-model\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"format\":{\"type\":\"object\"}", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"stream\":false", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_ollama_client_reports_an_unreachable_server_as_a_result()
    {
        var provider = new OllamaChatProvider(client: new HttpClient(new RecordingHandler(null)) { BaseAddress = new Uri("http://yerel.test") });

        var result = await provider.CompleteAsync(new ChatRequest([new("user", "x")]));

        Assert.Equal("ai.sunucuya_ulasilamadi", result.Error!.Code);
    }

    private sealed class ScriptedProvider(params string[] replies) : IChatProvider
    {
        private int _next;

        public List<ChatRequest> Requests { get; } = [];

        public Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default)
        {
            Requests.Add(request with { Messages = [.. request.Messages] });
            return Task.FromResult(_next < replies.Length
                ? Result.Success(replies[_next++])
                : Result.Failure<string>(new Error("ai.sunucuya_ulasilamadi", "sunucu yok")));
        }
    }

    private sealed class RecordingHandler(string? reply) : HttpMessageHandler
    {
        public string? Path { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (reply is null)
            {
                throw new HttpRequestException("bağlantı reddedildi");
            }

            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}
