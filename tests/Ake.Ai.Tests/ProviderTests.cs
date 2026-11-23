using System.Net;
using System.Text;

namespace Ake.Ai.Tests;

/// <summary>Sağlayıcı/model seçimi (ADR-0044): ayar dosyası, Ollama model listesi ve OpenAI uyumlu sağlayıcı.</summary>
public sealed class ProviderTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-ai-ayar-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Settings_round_trip_through_the_file_and_fall_back_to_defaults_when_missing_or_broken()
    {
        var path = Path.Combine(_directory.FullName, "alt", "yapay-zeka.json");
        Assert.Equal(AiSettings.Default, AiSettings.Load(path));

        var wanted = new AiSettings(AiSettings.OpenAi, "http://127.0.0.1:1234", "yerel-model");
        Assert.True(wanted.Save(path).IsSuccess);
        Assert.Equal(wanted, AiSettings.Load(path));

        File.WriteAllText(path, "{ bozuk");
        Assert.Equal(AiSettings.Default, AiSettings.Load(path));
        File.WriteAllText(path, """{ "provider": "uydurma", "address": "http://x", "model": "m" }""");
        Assert.Equal(AiSettings.Default, AiSettings.Load(path));
    }

    [Theory]
    [InlineData("ollama", "http://127.0.0.1:11434", "qwen3:8b", null)]
    [InlineData("openai", "https://api.ornek.com", "m", null)]
    [InlineData("uydurma", "http://127.0.0.1:11434", "m", "ai.saglayici_gecersiz")]
    [InlineData("ollama", "adres degil", "m", "ai.adres_gecersiz")]
    [InlineData("ollama", "ftp://x", "m", "ai.adres_gecersiz")]
    [InlineData("ollama", "http://127.0.0.1:11434", "  ", "ai.model_bos")]
    public void Settings_are_checked(string provider, string address, string model, string? code) =>
        Assert.Equal(code, new AiSettings(provider, address, model).Check()?.Code);

    [Theory]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://localhost:1234", true)]
    [InlineData("https://api.ornek.com", false)]
    [InlineData("http://192.168.1.20:11434", false)]
    public void Only_loopback_addresses_count_as_local(string address, bool local) =>
        Assert.Equal(local, new AiSettings(Address: address).IsLocal);

    [Fact]
    public async Task Ollama_models_are_listed_sorted_and_an_unreachable_server_is_a_result()
    {
        var handler = new Handler("""{ "models": [ { "name": "qwen3:8b" }, { "name": "llava:latest" } ] }""");
        var models = await OllamaChatProvider.ListModelsAsync(client: new HttpClient(handler) { BaseAddress = new Uri("http://yerel.test") });

        Assert.Equal(["llava:latest", "qwen3:8b"], models.Value);
        Assert.Equal("/api/tags", handler.Path);

        var down = await OllamaChatProvider.ListModelsAsync(client: new HttpClient(new Handler(null)) { BaseAddress = new Uri("http://yerel.test") });
        Assert.Equal("ai.sunucuya_ulasilamadi", down.Error!.Code);
    }

    [Fact]
    public async Task The_openai_compatible_provider_posts_the_schema_and_reads_the_first_choice()
    {
        var handler = new Handler("""{ "choices": [ { "message": { "role": "assistant", "content": "{\"commands\":[]}" } } ] }""");
        var provider = new OpenAiChatProvider("gpt-yerel", "http://yerel.test", new HttpClient(handler) { BaseAddress = new Uri("http://yerel.test") });

        var result = await provider.CompleteAsync(new ChatRequest([new("system", "s"), new("user", "u")], """{ "type": "object" }"""));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("{\"commands\":[]}", result.Value);
        Assert.Equal("/v1/chat/completions", handler.Path);
        Assert.Contains("\"model\":\"gpt-yerel\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"response_format\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"json_schema\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_openai_compatible_provider_lists_models_and_reports_server_errors()
    {
        var models = await OpenAiChatProvider.ListModelsAsync(
            "http://yerel.test", new HttpClient(new Handler("""{ "data": [ { "id": "b" }, { "id": "a" } ] }""")) { BaseAddress = new Uri("http://yerel.test") });
        Assert.Equal(["a", "b"], models.Value);

        var failing = new OpenAiChatProvider("m", "http://yerel.test", new HttpClient(new Handler("hata", HttpStatusCode.Unauthorized)) { BaseAddress = new Uri("http://yerel.test") });
        Assert.Equal("ai.sunucu_hatasi", (await failing.CompleteAsync(new ChatRequest([new("user", "x")]))).Error!.Code);
    }

    [Fact]
    public void The_chosen_provider_is_created_from_the_settings()
    {
        Assert.IsType<OllamaChatProvider>(ChatProviders.Create(AiSettings.Default));
        Assert.IsType<OpenAiChatProvider>(ChatProviders.Create(new AiSettings(AiSettings.OpenAi, "http://127.0.0.1:1234", "m")));
    }

    [Fact]
    public void A_patch_is_described_one_readable_line_per_command()
    {
        const string json = """
            { "commands": [
              { "op": "addDevice", "tag": "=M1+P1-K7", "description": "Kontaktör" },
              { "op": "connect", "from": "-K7:A2", "to": "-T1:-", "wire": { "number": "12" } },
              { "op": "disconnect", "from": "-K1:A1", "to": "-T1:1" },
              { "op": "retag", "device": "-K1", "tag": "-K9" },
              { "op": "removeDevice", "device": "-K3" }
            ] }
            """;

        Assert.Equal(
            [
                "Cihaz ekle: =M1+P1-K7 (Kontaktör)",
                "Bağla: -K7:A2 → -T1:- (tel 12)",
                "Bağlantıyı kaldır: -K1:A1 → -T1:1",
                "Etiketi değiştir: -K1 → -K9",
                "Cihazı sil: -K3",
            ],
            PatchSummary.Describe(json));
        Assert.Empty(PatchSummary.Describe("bozuk"));
    }

    [Fact]
    public void The_output_schema_has_one_strict_option_per_patch_command()
    {
        var options = System.Text.Json.Nodes.JsonNode.Parse(PatchPrompt.Schema)!["properties"]!["commands"]!["items"]!["anyOf"]!.AsArray();
        var required = options.ToDictionary(
            option => option!["properties"]!["op"]!["const"]!.GetValue<string>(),
            option => option!["required"]!.AsArray().Select(name => name!.GetValue<string>()).ToList());

        Assert.Equal(
            ["addDevice", "removeDevice", "connect", "disconnect", "assignWire", "retag", "assignIo", "setProperty", "addPage", "assignSymbol"],
            required.Keys);
        Assert.Equal(["op", "device", "tag"], required["retag"]);
        Assert.Equal(["op", "from", "to"], required["disconnect"]);
        Assert.All(options, option => Assert.False(option!["additionalProperties"]!.GetValue<bool>()));
        Assert.Contains("PlcChannel", PatchPrompt.Schema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_openai_compatible_provider_refuses_to_send_api_key_over_remote_plaintext_http()
    {
        var previous = Environment.GetEnvironmentVariable(OpenAiChatProvider.KeyVariable);
        Environment.SetEnvironmentVariable(OpenAiChatProvider.KeyVariable, "gizli-test-anahtari");
        try
        {
            const string okReply = """{ "choices": [ { "message": { "role": "assistant", "content": "{\"commands\":[]}" } } ] }""";

            var httpHandler = new Handler(okReply);
            var httpRemote = new OpenAiChatProvider("m", "http://example.com", new HttpClient(httpHandler) { BaseAddress = new Uri("http://example.com") });
            Assert.True((await httpRemote.CompleteAsync(new ChatRequest([new("user", "u")]))).IsSuccess);
            Assert.Null(httpHandler.Authorization);

            var httpsHandler = new Handler(okReply);
            var httpsRemote = new OpenAiChatProvider("m", "https://api.example.com", new HttpClient(httpsHandler) { BaseAddress = new Uri("https://api.example.com") });
            Assert.True((await httpsRemote.CompleteAsync(new ChatRequest([new("user", "u")]))).IsSuccess);
            Assert.Equal("Bearer gizli-test-anahtari", httpsHandler.Authorization);

            var localHandler = new Handler(okReply);
            var localLoopback = new OpenAiChatProvider("m", "http://127.0.0.1:1234", new HttpClient(localHandler) { BaseAddress = new Uri("http://127.0.0.1:1234") });
            Assert.True((await localLoopback.CompleteAsync(new ChatRequest([new("user", "u")]))).IsSuccess);
            Assert.Equal("Bearer gizli-test-anahtari", localHandler.Authorization);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiChatProvider.KeyVariable, previous);
        }
    }

    [Fact]
    public void Claude_and_Antigravity_settings_validate_and_save_never_writes_api_key()
    {
        Assert.Null(new AiSettings(AiSettings.Claude, AnthropicChatProvider.DefaultAddress, AnthropicChatProvider.DefaultModel, "sk-ant-gizli-123").Check());
        Assert.Null(new AiSettings(AiSettings.Antigravity, AntigravityChatProvider.DefaultAddress, AntigravityChatProvider.DefaultModel, "AIza-gizli-456").Check());

        var path = Path.Combine(_directory.FullName, "claude-ve-antigravity.json");
        var withKey = new AiSettings(AiSettings.Claude, AnthropicChatProvider.DefaultAddress, AnthropicChatProvider.DefaultModel, "sk-ant-gizli-123");
        Assert.True(withKey.Save(path).IsSuccess);

        var written = File.ReadAllText(path);
        Assert.DoesNotContain("sk-ant-gizli-123", written, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", written, StringComparison.OrdinalIgnoreCase);

        var loaded = AiSettings.Load(path);
        Assert.Equal(AiSettings.Claude, loaded.Provider);
        Assert.Null(loaded.ApiKey);
    }

    [Fact]
    public async Task Anthropic_provider_sends_messages_payload_strips_fences_and_guards_key_over_plain_http()
    {
        const string okReply = """{ "content": [ { "type": "text", "text": "```json\n{\"commands\":[]}\n```" } ] }""";
        var request = new ChatRequest([new("system", "kurallar"), new("user", "K7 ekle")], """{"type":"object"}""");

        var httpsHandler = new Handler(okReply);
        var claudeHttps = new AnthropicChatProvider(
            AnthropicChatProvider.DefaultModel,
            AnthropicChatProvider.DefaultAddress,
            new HttpClient(httpsHandler) { BaseAddress = new Uri(AnthropicChatProvider.DefaultAddress) },
            apiKey: "sk-ant-oturum");

        var reply = await claudeHttps.CompleteAsync(request);
        Assert.True(reply.IsSuccess);
        Assert.Equal("{\"commands\":[]}", reply.Value);
        Assert.Equal("/v1/messages", httpsHandler.Path);
        Assert.Equal("sk-ant-oturum", httpsHandler.AnthropicKey);
        Assert.Equal(AnthropicChatProvider.ApiVersion, httpsHandler.AnthropicVersion);
        Assert.Contains("\"system\":", httpsHandler.Body, StringComparison.Ordinal);
        Assert.Contains("\"messages\":[{\"role\":\"user\",\"content\":\"K7 ekle\"}]", httpsHandler.Body, StringComparison.Ordinal);

        // Güvensiz uzak http:// adresine oturum anahtarı dahi gönderilmez; loopback http:// izin verilir.
        var httpHandler = new Handler(okReply);
        var remoteHttp = new AnthropicChatProvider("claude-sonnet-4-5", "http://example.com", new HttpClient(httpHandler) { BaseAddress = new Uri("http://example.com") }, "sk-ant-oturum");
        Assert.True((await remoteHttp.CompleteAsync(new ChatRequest([new("user", "u")]))).IsSuccess);
        Assert.Null(httpHandler.AnthropicKey);

        var localHandler = new Handler(okReply);
        var localHttp = new AnthropicChatProvider("claude-sonnet-4-5", "http://127.0.0.1:4321", new HttpClient(localHandler) { BaseAddress = new Uri("http://127.0.0.1:4321") }, "sk-ant-oturum");
        Assert.True((await localHttp.CompleteAsync(new ChatRequest([new("user", "u")]))).IsSuccess);
        Assert.Equal("sk-ant-oturum", localHandler.AnthropicKey);
    }

    [Fact]
    public async Task Antigravity_provider_uses_openai_path_without_double_v1_reads_env_key_and_guards_transport()
    {
        var previous = Environment.GetEnvironmentVariable(AntigravityChatProvider.KeyVariable);
        Environment.SetEnvironmentVariable(AntigravityChatProvider.KeyVariable, "ag-env-anahtar");
        try
        {
            const string okReply = """{ "choices": [ { "message": { "role": "assistant", "content": "{\"commands\":[]}" } } ] }""";

            var httpsHandler = new Handler(okReply);
            var agHttps = new AntigravityChatProvider(
                AntigravityChatProvider.DefaultModel,
                AntigravityChatProvider.DefaultAddress,
                new HttpClient(httpsHandler) { BaseAddress = new Uri(AntigravityChatProvider.DefaultAddress) });
            var reply = await agHttps.CompleteAsync(new ChatRequest([new("user", "bağla")]));
            Assert.True(reply.IsSuccess);
            Assert.Equal("/v1beta/openai/chat/completions", httpsHandler.Path);
            Assert.Equal("Bearer ag-env-anahtar", httpsHandler.Authorization);

            var httpHandler = new Handler(okReply);
            var remoteHttp = new AntigravityChatProvider("gemini-2.5-pro", "http://example.com", new HttpClient(httpHandler) { BaseAddress = new Uri("http://example.com") });
            Assert.True((await remoteHttp.CompleteAsync(new ChatRequest([new("user", "u")]))).IsSuccess);
            Assert.Null(httpHandler.Authorization);

            var localHandler = new Handler(okReply);
            var localHttp = new AntigravityChatProvider("gemini-2.5-pro", "http://127.0.0.1:8045", new HttpClient(localHandler) { BaseAddress = new Uri("http://127.0.0.1:8045") });
            Assert.True((await localHttp.CompleteAsync(new ChatRequest([new("user", "u")]))).IsSuccess);
            Assert.Equal("Bearer ag-env-anahtar", localHandler.Authorization);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AntigravityChatProvider.KeyVariable, previous);
        }
    }

    private sealed class Handler(string? reply, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }

        public string? Body { get; private set; }

        public string? Authorization { get; private set; }

        public string? AnthropicKey { get; private set; }

        public string? AnthropicVersion { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (reply is null)
            {
                throw new HttpRequestException("bağlantı reddedildi");
            }

            Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            AnthropicKey = request.Headers.TryGetValues("x-api-key", out var k) ? string.Join(",", k) : null;
            AnthropicVersion = request.Headers.TryGetValues("anthropic-version", out var v) ? string.Join(",", v) : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}
