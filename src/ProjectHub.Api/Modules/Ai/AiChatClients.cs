using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using OpenAI;
using BetaMessages = Anthropic.Models.Beta.Messages;

namespace ProjectHub.Api.Modules.Ai;

/// <summary>
/// The model behind the assistant, provider-neutral (ADR 0015): <see cref="Client"/> is a Microsoft.Extensions.AI
/// chat client, <see cref="Configure"/> adds what only this provider understands to a request.
/// </summary>
public sealed record AiModel(IChatClient Client, string Provider, string? ModelId, Action<ChatOptions> Configure);

public static class AiChatClients
{
    /// <summary>Source name of the GenAI spans and metrics (OpenTelemetry semantic conventions); prompts are never exported.</summary>
    public const string TelemetrySourceName = "ProjectHub.Ai";

    /// <summary>Models that take the server-side refusal fallback ("fallbacks": "default").</summary>
    private static readonly HashSet<string> RefusalFallbackModels = ["claude-opus-5-5", "claude-opus-5", "claude-fable-5-1", "claude-sonnet-5-5"];

    /// <param name="handler">Replaces the network for the provider's HTTP calls (tests).</param>
    public static AiModel Create(AiOptions options, ILoggerFactory loggerFactory, HttpMessageHandler? handler = null)
    {
        var (inner, configure) = options.Provider switch
        {
            AiOptions.Anthropic => Claude(options, handler),
            AiOptions.OpenAi => OpenAiCompatible(options, handler),
            AiOptions.Fake => (new FakeChatClient(), _ => { }),
            _ => throw new InvalidOperationException("The AI assistant is switched off."),
        };

        var client = new ChatClientBuilder(inner)
            .UseFunctionInvocation(loggerFactory, invoking =>
            {
                // Tools share the request's DbContext, so they run one after another; a few rounds are enough to look things up.
                invoking.AllowConcurrentInvocation = false;
                invoking.MaximumIterationsPerRequest = 6;
                invoking.IncludeDetailedErrors = false;
            })
            .UseOpenTelemetry(loggerFactory, TelemetrySourceName, telemetry => telemetry.EnableSensitiveData = false)
            .Build();
        return new AiModel(client, options.Provider, options.Model, configure);
    }

    /// <summary>Claude through the official Anthropic SDK, with adaptive thinking, effort, prompt caching and refusal fallback.</summary>
    private static (IChatClient, Action<ChatOptions>) Claude(AiOptions options, HttpMessageHandler? handler)
    {
        var clientOptions = new ClientOptions { ApiKey = options.ApiKey! };
        if (options.BaseUrl is { } baseUrl)
        {
            clientOptions.BaseUrl = baseUrl.ToString().TrimEnd('/');
        }

        if (handler is not null)
        {
            clientOptions.HttpClient = new HttpClient(handler);
        }

        var anthropic = new AnthropicClient(clientOptions);

        var model = options.Model!;
        var fallback = RefusalFallbackModels.Contains(model);
        var effort = options.Effort switch
        {
            "low" => BetaMessages.Effort.Low,
            "high" => BetaMessages.Effort.High,
            "xhigh" => BetaMessages.Effort.Xhigh,
            "max" => BetaMessages.Effort.Max,
            _ => BetaMessages.Effort.Medium,
        };

        void Configure(ChatOptions chat) =>
            chat.RawRepresentationFactory = _ =>
            {
                var request = new BetaMessages.MessageCreateParams
                {
                    Model = model,
                    MaxTokens = chat.MaxOutputTokens ?? options.MaxOutputTokens,
                    Messages = [],
                    OutputConfig = new BetaMessages.BetaOutputConfig { Effort = effort },

                    // Tools and instructions are the same for every request: cache them (top-level automatic caching).
                    CacheControl = new BetaMessages.BetaCacheControlEphemeral(),
                };

                // A declined request is answered by the model the API picks for that case, within the same call.
                return fallback
                    ? request with
                    {
                        Betas = ["server-side-fallback-2026-07-01"],
                        Fallbacks = new BetaMessages.BetaFallbacksParam(new BetaMessages.Default()),
                    }
                    : request;
            };

        return (anthropic.Beta.AsIChatClient(model, options.MaxOutputTokens), Configure);
    }

    /// <summary>
    /// Any OpenAI-compatible endpoint: OpenAI, Azure OpenAI (v1 endpoint), or a model on the own server
    /// (Ollama, vLLM, LM Studio), which keeps all data in-house.
    /// </summary>
    private static (IChatClient, Action<ChatOptions>) OpenAiCompatible(AiOptions options, HttpMessageHandler? handler)
    {
        var clientOptions = new OpenAIClientOptions();
        if (handler is not null)
        {
            clientOptions.Transport = new HttpClientPipelineTransport(new HttpClient(handler));
        }

        if (options.BaseUrl is { } baseUrl)
        {
            clientOptions.Endpoint = baseUrl;
        }

        var client = new OpenAIClient(new ApiKeyCredential(options.ApiKey ?? "unused"), clientOptions);
        var effort = options.Effort switch
        {
            "low" => ReasoningEffort.Low,
            "high" => ReasoningEffort.High,
            "xhigh" or "max" => ReasoningEffort.ExtraHigh,
            _ => ReasoningEffort.Medium,
        };

        // Local models often do not reason; the effort is only sent where a reasoning model is configured on purpose.
        var sendEffort = options.BaseUrl is null;
        return (client.GetChatClient(options.Model!).AsIChatClient(), chat =>
        {
            if (sendEffort)
            {
                chat.Reasoning = new ReasoningOptions { Effort = effort };
            }
        });
    }
}

/// <summary>
/// Deterministic model for Development and tests (no network, no key): looks the question up with
/// <c>search_knowledge</c> and answers with links to the passages it found, so the whole tool loop runs.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    public const string Marker = "(Testantwort ohne Sprachmodell)";

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var toolResult = history.LastOrDefault()?.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        if (toolResult is null)
        {
            var question = history.LastOrDefault(m => m.Role == ChatRole.User)?.Contents.OfType<TextContent>().LastOrDefault()?.Text ?? string.Empty;
            var hasSearch = options?.Tools?.Any(t => t.Name == "search_knowledge") == true;
            if (hasSearch && !string.IsNullOrWhiteSpace(question))
            {
                var call = new FunctionCallContent($"call_{Guid.NewGuid():N}", "search_knowledge", new Dictionary<string, object?> { ["query"] = question, ["limit"] = 3 });
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { FinishReason = ChatFinishReason.ToolCalls });
            }
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(toolResult))) { FinishReason = ChatFinishReason.Stop });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            if (update.Contents is [TextContent text])
            {
                // Word by word, like a real model streams.
                foreach (var word in text.Text.Split(' '))
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ") { MessageId = update.MessageId };
                }
            }
            else
            {
                yield return update;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static string Answer(FunctionResultContent? toolResult)
    {
        var result = toolResult?.Result is { } value ? JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions) : default;
        var hits = result is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Select(e => (Id: e.GetProperty("articleId").GetString(), Title: e.GetProperty("title").GetString())).ToList()
            : [];
        var answer = new StringBuilder(Marker).Append(' ');
        answer.Append(hits.Count == 0
            ? "Dazu habe ich im Wissen nichts gefunden."
            : "Passend dazu: " + string.Join(", ", hits.Select(h => $"[{h.Title}](article:{h.Id})")) + ".");
        return answer.ToString();
    }
}
