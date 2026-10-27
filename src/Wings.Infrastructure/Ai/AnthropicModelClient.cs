using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wings.Application.Ai;

namespace Wings.Infrastructure.Ai;

/// <summary>
/// Claude through the official Anthropic SDK. Answers are constrained to the caller's JSON Schema (structured outputs); refused requests
/// are re-served on a fallback model server-side when <see cref="AiOptions.RefusalFallbacks"/> is on.
/// </summary>
public sealed class AnthropicModelClient : IAiModelClient
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";
    private readonly AnthropicClient? _client;
    private readonly AiOptions _options;
    private readonly ILogger<AnthropicModelClient> _log;

    public AnthropicModelClient(IOptions<AiOptions> options, ILogger<AnthropicModelClient> log)
    {
        _options = options.Value;
        _log = log;
        var key = _options.ApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Unavailable = "No AI provider key is configured. Set the ANTHROPIC_API_KEY environment variable (or Ai:ApiKey in a secret store) on the server.";
            return;
        }
        _client = string.IsNullOrWhiteSpace(_options.BaseUrl) ? new AnthropicClient { ApiKey = key } : new AnthropicClient { ApiKey = key, BaseUrl = _options.BaseUrl };
    }

    public string Provider => "Anthropic";
    public string Model => _options.Model;
    public string? Unavailable { get; }

    public async Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken ct)
    {
        if (_client is null) throw new AiProviderException(Unavailable!);
        var content = new List<BetaContentBlockParam>();
        foreach (var image in request.Images)
        {
            content.Add(new BetaTextBlockParam { Text = $"Screenshot: {image.Label}" });
            content.Add(new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(image.Data), MediaType = image.MediaType } });
        }
        content.Add(new BetaTextBlockParam { Text = request.UserText });

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = request.MaxTokens ?? _options.MaxTokens,
            System = request.System,
            Messages = [new BetaUserMessageParam { Content = content }],
            OutputConfig = new BetaOutputConfig
            {
                Effort = _options.Effort,
                Format = new BetaJsonOutputFormat { Schema = request.Schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) },
            },
            Betas = _options.RefusalFallbacks ? [FallbackBeta] : null,
            // "default" routes each refusal category to a suitable fallback model, so no model list is maintained here.
            Fallbacks = _options.RefusalFallbacks ? new Default() : null,
        };

        BetaMessage message;
        try
        {
            message = await _client.Beta.Messages.Create(parameters, ct);
        }
        catch (AnthropicRateLimitException ex) { throw Fail("The AI provider is rate limiting requests; try again shortly.", ex); }
        catch (AnthropicUnauthorizedException ex) { throw Fail("The AI provider rejected the configured key.", ex); }
        catch (AnthropicBadRequestException ex) { throw Fail($"The AI provider rejected the request: {ex.Message}", ex); }
        catch (Anthropic5xxException ex) { throw Fail("The AI provider had a server error; try again shortly.", ex); }
        catch (AnthropicApiException ex) { throw Fail($"The AI provider returned an error: {ex.Message}", ex); }
        catch (AnthropicIOException ex) { throw Fail("The AI provider could not be reached.", ex); }

        if (message.StopReason == "refusal")
            throw new AiRefusedException(message.StopDetails?.Explanation ?? "The model declined to answer.");
        if (message.StopReason == "max_tokens")
            throw new AiProviderException("The answer was longer than the configured maximum (Ai:MaxTokens); narrow the request or raise the limit.");

        var json = new StringBuilder();
        foreach (var block in message.Content)
            if (block.TryPickText(out var text)) json.Append(text.Text);
        return new AiModelResponse(json.ToString(), message.Model, message.Usage.InputTokens, message.Usage.OutputTokens);
    }

    private AiProviderException Fail(string message, Exception ex)
    {
        _log.LogWarning(ex, "AI provider call failed");
        return new AiProviderException(message);
    }
}
