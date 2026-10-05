namespace ProjectHub.Api.Modules.Ai;

/// <summary>
/// Which language model the assistant uses and how (ADR 0015). Keys come from the environment (.env); API keys only
/// from the secret store. The MCP server (<see cref="McpEnabled"/>) does not need a model: agents bring their own.
/// </summary>
public sealed record AiOptions(
    string Provider,
    string? Model,
    string? ApiKey,
    Uri? BaseUrl,
    string Effort,
    int MaxOutputTokens,
    int RequestsPerMinute,
    bool McpEnabled,
    bool McpWriteTools,
    bool AssistantWriteTools = true)
{
    public const string ProviderKey = "PROJECTHUB_AI_PROVIDER";
    public const string ModelKey = "PROJECTHUB_AI_MODEL";

    /// <summary>Key for Claude (provider anthropic); the official SDK reads the same variable.</summary>
    public const string AnthropicApiKeyKey = "ANTHROPIC_API_KEY";

    /// <summary>Key for an OpenAI-compatible endpoint (provider openai); optional for local servers such as Ollama.</summary>
    public const string ApiKeyKey = "PROJECTHUB_AI_API_KEY";

    /// <summary>OpenAI-compatible endpoint (e.g. http://ollama:11434/v1), or a gateway in front of the Claude API.</summary>
    public const string BaseUrlKey = "PROJECTHUB_AI_BASE_URL";

    public const string EffortKey = "PROJECTHUB_AI_EFFORT";
    public const string MaxOutputTokensKey = "PROJECTHUB_AI_MAX_OUTPUT_TOKENS";
    public const string RequestsPerMinuteKey = "PROJECTHUB_AI_RATE_LIMIT_PER_MINUTE";
    public const string McpKey = "PROJECTHUB_MCP";
    public const string McpWriteToolsKey = "PROJECTHUB_MCP_WRITE_TOOLS";

    /// <summary>Whether the assistant may propose changes; each one needs the person's approval (ADR 0016).</summary>
    public const string AssistantWriteToolsKey = "PROJECTHUB_AI_WRITE_TOOLS";

    public const string Off = "off";
    public const string Fake = "fake";
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";

    /// <summary>The current Claude model; replaced by setting <see cref="ModelKey"/>, no code change needed.</summary>
    public const string DefaultClaudeModel = "claude-opus-5-5";

    public static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    public bool AssistantEnabled => Provider != Off;

    public static AiOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        static string? Secret(string? value) =>
            string.IsNullOrWhiteSpace(value) || value.StartsWith("__", StringComparison.Ordinal) ? null : value.Trim();

        var provider = (configuration[ProviderKey] is { Length: > 0 } configured ? configured : environment.IsDevelopment() ? Fake : Off)
            .Trim().ToLowerInvariant();
        if (provider is not (Off or Fake or Anthropic or OpenAi))
        {
            throw new InvalidOperationException($"{ProviderKey} must be '{Off}', '{Fake}', '{Anthropic}' or '{OpenAi}'.");
        }

        var model = configuration[ModelKey] is { Length: > 0 } m ? m.Trim() : provider == Anthropic ? DefaultClaudeModel : null;
        if (provider == OpenAi && model is null)
        {
            throw new InvalidOperationException($"{ModelKey} is required when {ProviderKey} is '{OpenAi}'.");
        }

        var apiKey = provider == Anthropic ? Secret(configuration[AnthropicApiKeyKey]) : Secret(configuration[ApiKeyKey]);
        if (provider == Anthropic && apiKey is null)
        {
            throw new InvalidOperationException($"{AnthropicApiKeyKey} is required when {ProviderKey} is '{Anthropic}'.");
        }

        Uri? baseUrl = null;
        if (configuration[BaseUrlKey] is { Length: > 0 } url
            && (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out baseUrl) || baseUrl.Scheme is not ("http" or "https")))
        {
            throw new InvalidOperationException($"{BaseUrlKey} must be an absolute http(s) URL.");
        }

        if (provider == OpenAi && baseUrl is null && apiKey is null)
        {
            throw new InvalidOperationException($"{ProviderKey} '{OpenAi}' needs {BaseUrlKey} (local server) or {ApiKeyKey} (OpenAI).");
        }

        var effort = (configuration[EffortKey] is { Length: > 0 } e ? e : "medium").Trim().ToLowerInvariant();
        if (!Efforts.Contains(effort))
        {
            throw new InvalidOperationException($"{EffortKey} must be one of {string.Join(", ", Efforts)}.");
        }

        var maxOutputTokens = configuration.GetValue(MaxOutputTokensKey, 16_000);
        var requestsPerMinute = configuration.GetValue(RequestsPerMinuteKey, 10);
        if (maxOutputTokens is < 256 or > 128_000 || requestsPerMinute < 0)
        {
            throw new InvalidOperationException($"{MaxOutputTokensKey} must be between 256 and 128000; {RequestsPerMinuteKey} must not be negative.");
        }

        var mcp = (configuration[McpKey] is { Length: > 0 } flag ? flag : environment.IsDevelopment() ? "on" : Off).Trim().ToLowerInvariant();
        if (mcp is not ("on" or Off))
        {
            throw new InvalidOperationException($"{McpKey} must be 'on' or '{Off}'.");
        }

        return new AiOptions(
            provider, model, apiKey, baseUrl, effort, maxOutputTokens, requestsPerMinute, mcp == "on", configuration.GetValue(McpWriteToolsKey, false),
            configuration.GetValue(AssistantWriteToolsKey, true));
    }
}
