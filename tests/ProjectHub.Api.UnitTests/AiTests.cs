using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectHub.Api.Modules.Ai;
using ProjectHub.Api.Modules.Knowledge;

namespace ProjectHub.Api.UnitTests;

public sealed class AiOptionsTests
{
    [Fact]
    public void Defaults_to_the_fake_model_in_development_and_off_elsewhere()
    {
        Assert.Equal(AiOptions.Fake, Options([], "Development").Provider);
        var production = Options([], "Production");
        Assert.Equal(AiOptions.Off, production.Provider);
        Assert.False(production.AssistantEnabled);
        Assert.False(production.McpEnabled);
    }

    [Fact]
    public void Claude_defaults_to_the_current_opus_model_with_medium_effort()
    {
        var options = Options(new() { [AiOptions.ProviderKey] = "anthropic", [AiOptions.AnthropicApiKeyKey] = "sk-test" });

        Assert.Equal("claude-opus-5-5", options.Model);
        Assert.Equal("medium", options.Effort);
        Assert.Equal("sk-test", options.ApiKey);
    }

    [Theory]
    [InlineData("anthropic", null, null, null)]
    [InlineData("anthropic", "__SET_VIA_SECRET_STORE__", null, null)]
    [InlineData("openai", null, "http://ollama:11434/v1", null)]
    [InlineData("openai", null, null, "gpt-test")]
    [InlineData("gemini", null, null, null)]
    public void Rejects_incomplete_provider_settings(string provider, string? anthropicKey, string? baseUrl, string? model)
    {
        var values = new Dictionary<string, string?>
        {
            [AiOptions.ProviderKey] = provider,
            [AiOptions.AnthropicApiKeyKey] = anthropicKey,
            [AiOptions.BaseUrlKey] = baseUrl,
            [AiOptions.ModelKey] = model,
        };

        Assert.Throws<InvalidOperationException>(() => Options(values));
    }

    [Fact]
    public void A_local_model_needs_no_key()
    {
        var options = Options(new()
        {
            [AiOptions.ProviderKey] = "openai",
            [AiOptions.BaseUrlKey] = "http://ollama:11434/v1",
            [AiOptions.ModelKey] = "qwen3:32b",
        });

        Assert.Null(options.ApiKey);
        Assert.Equal(new Uri("http://ollama:11434/v1"), options.BaseUrl);
    }

    [Theory]
    [InlineData(AiOptions.EffortKey, "extreme")]
    [InlineData(AiOptions.MaxOutputTokensKey, "100")]
    [InlineData(AiOptions.RequestsPerMinuteKey, "-1")]
    [InlineData(AiOptions.McpKey, "yes")]
    [InlineData(AiOptions.BaseUrlKey, "ftp://example")]
    public void Rejects_invalid_values(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => Options(new() { [key] = value }));

    internal static AiOptions Options(Dictionary<string, string?> values, string environment = "Development") =>
        AiOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Environment(environment));

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ProjectHub.Api";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

public sealed class AiChatClientTests
{
    [Fact]
    public async Task Claude_requests_use_effort_caching_refusal_fallback_and_stream()
    {
        var handler = new ScriptedHandler(_ => Sse(TextAnswer("Hallo")));
        var model = Claude(handler);

        var text = await AnswerAsync(model, "Hallo?");

        Assert.Equal("Hallo", text);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/v1/messages?beta=true", request.Uri);
        Assert.Contains("server-side-fallback-2026-07-01", request.Betas);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal("default", root.GetProperty("fallbacks").GetString());
        Assert.Equal("medium", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("ephemeral", root.GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal(16_000, root.GetProperty("max_tokens").GetInt32());
        Assert.Contains("ProjectHub", root.GetProperty("system").ToString());
        Assert.Equal("lookup", root.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task Claude_tool_calls_run_the_tool_and_send_thinking_and_result_back()
    {
        var handler = new ScriptedHandler(call => call == 0 ? Sse(ToolUse("toolu_1", "lookup", """{"query":"Kickoff"}""")) : Sse(TextAnswer("Siehe Kickoff.")));
        var model = Claude(handler);

        var text = await AnswerAsync(model, "Wie läuft ein Kickoff?");

        Assert.Equal("Siehe Kickoff.", text);
        Assert.Equal(2, handler.Requests.Count);
        using var second = JsonDocument.Parse(handler.Requests[1].Body);
        var messages = second.RootElement.GetProperty("messages");
        var assistant = messages[messages.GetArrayLength() - 2].GetProperty("content");
        Assert.Equal("thinking", assistant[0].GetProperty("type").GetString());
        Assert.Equal("sig-1", assistant[0].GetProperty("signature").GetString());
        Assert.Equal("tool_use", assistant[1].GetProperty("type").GetString());
        var toolResult = messages[messages.GetArrayLength() - 1].GetProperty("content")[0];
        Assert.Equal("tool_result", toolResult.GetProperty("type").GetString());
        Assert.Equal("toolu_1", toolResult.GetProperty("tool_use_id").GetString());
        Assert.Contains("Kickoff gefunden", toolResult.GetProperty("content").ToString());
    }

    [Fact]
    public async Task Models_without_the_server_side_fallback_send_no_fallback()
    {
        var handler = new ScriptedHandler(_ => Sse(TextAnswer("ok")));
        var model = Claude(handler, "claude-haiku-4-5");

        await AnswerAsync(model, "Hallo?");

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.False(body.RootElement.TryGetProperty("fallbacks", out _));
        Assert.DoesNotContain("server-side-fallback", handler.Requests[0].Betas);
    }

    [Fact]
    public async Task OpenAi_compatible_endpoints_get_a_chat_completions_request()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"id":"c1","object":"chat.completion","created":1,"model":"qwen3","choices":[{"index":0,"message":{"role":"assistant","content":"Hallo lokal"},"finish_reason":"stop"}]}""",
                Encoding.UTF8, "application/json"),
        });
        var options = AiOptionsTests.Options(new()
        {
            [AiOptions.ProviderKey] = "openai",
            [AiOptions.BaseUrlKey] = "http://ollama:11434/v1",
            [AiOptions.ModelKey] = "qwen3",
        });
        var model = AiChatClients.Create(options, NullLoggerFactory.Instance, handler);
        var chat = new ChatOptions();
        model.Configure(chat);

        var response = await model.Client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hallo?")], chat);

        Assert.Equal("Hallo lokal", response.Text);
        Assert.Equal("http://ollama:11434/v1/chat/completions", handler.Requests[0].Uri);
        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("qwen3", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public void Knowledge_questions_become_an_or_query_of_plain_lexemes()
    {
        Assert.Equal("'wie' | 'läuft' | 'ein' | 'kickoff' | 'ab'", PostgresKnowledgeRetrieval.TermQuery("Wie läuft ein Kickoff ab?"));
        Assert.Equal("'drop' | 'table' | 'xy'", PostgresKnowledgeRetrieval.TermQuery("'); DROP TABLE xy; -- & | !"));
        Assert.Null(PostgresKnowledgeRetrieval.TermQuery("?! a"));
    }

    private static AiModel Claude(HttpMessageHandler handler, string? model = null)
    {
        var values = new Dictionary<string, string?> { [AiOptions.ProviderKey] = "anthropic", [AiOptions.AnthropicApiKeyKey] = "sk-test" };
        if (model is not null)
        {
            values[AiOptions.ModelKey] = model;
        }

        return AiChatClients.Create(AiOptionsTests.Options(values), NullLoggerFactory.Instance, handler);
    }

    private static async Task<string> AnswerAsync(AiModel model, string question)
    {
        var chat = new ChatOptions
        {
            Instructions = "Du bist der Assistent von ProjectHub.",
            Tools = [AIFunctionFactory.Create((string query) => $"Artikel zu {query} gefunden", "lookup", "Looks something up.")],
        };
        model.Configure(chat);
        var text = new StringBuilder();
        await foreach (var update in model.Client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, question)], chat))
        {
            text.Append(update.Text);
        }

        return text.ToString();
    }

    private static string TextAnswer(string text) => Events(
        """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
        $$$"""{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":{{{JsonSerializer.Serialize(text)}}}}}""",
        """{"type":"content_block_stop","index":0}""",
        """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":5}}""");

    /// <summary>A tool call after a thinking block, as current Claude models send it (thinking is always on).</summary>
    private static string ToolUse(string id, string name, string input) => Events(
        """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""",
        """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-1"}}""",
        """{"type":"content_block_stop","index":0}""",
        """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":""" + JsonSerializer.Serialize(id) + ""","name":""" + JsonSerializer.Serialize(name) + ""","input":{}}}""",
        $$$"""{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":{{{JsonSerializer.Serialize(input)}}}}}""",
        """{"type":"content_block_stop","index":1}""",
        """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":5}}""");

    private static string Events(params string[] blocks)
    {
        var all = new List<string>
        {
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}""",
        };
        all.AddRange(blocks);
        all.Add("""{"type":"message_stop"}""");
        return string.Concat(all.Select(json => $"event: {JsonDocument.Parse(json).RootElement.GetProperty("type").GetString()}\ndata: {json}\n\n"));
    }

    private static HttpResponseMessage Sse(string events) =>
        new(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };

    private sealed record RecordedRequest(string Uri, string Betas, string Body);

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var betas = request.Headers.TryGetValues("anthropic-beta", out var values) ? string.Join(",", values) : "";
            Requests.Add(new RecordedRequest(request.RequestUri!.AbsoluteUri, betas, body));
            return respond(Requests.Count - 1);
        }
    }
}
