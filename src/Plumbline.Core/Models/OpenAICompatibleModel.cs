using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plumbline.Models;

/// <summary>
/// Calls any OpenAI-compatible <c>/chat/completions</c> endpoint: Ollama, Groq, OpenAI,
/// Azure OpenAI's v1 API, LM Studio, vLLM. Retries 429 and 5xx responses with backoff.
/// </summary>
public sealed class OpenAICompatibleModel : IChatModel
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string? _apiKey;
    private readonly bool _azureKeyHeader;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;

    public OpenAICompatibleModel(
        HttpClient http,
        Uri baseUrl,
        string? apiKey = null,
        int maxRetries = 2,
        TimeSpan? retryDelay = null)
    {
        _http = http;
        _endpoint = new Uri(baseUrl.ToString().TrimEnd('/') + "/chat/completions");
        _apiKey = apiKey;
        // Azure OpenAI takes keys in an "api-key" header; everyone else uses a bearer token.
        _azureKeyHeader = baseUrl.Host.EndsWith(".azure.com", StringComparison.OrdinalIgnoreCase);
        _maxRetries = maxRetries;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var body = BuildBody(request);

        for (var attempt = 0; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            AddAuth(message);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(message, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new ModelException($"Could not reach {_endpoint}: {ex.Message}", ex);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return ParseReply(text);
                }

                if (IsRetryable(response.StatusCode) && attempt < _maxRetries)
                {
                    await Task.Delay(_retryDelay * (attempt + 1), cancellationToken);
                    continue;
                }

                throw new ModelException(
                    $"{(int)response.StatusCode} {response.ReasonPhrase} from {_endpoint}: {Truncate(text, 300)}");
            }
        }
    }

    internal static string BuildBody(ModelRequest request)
    {
        var messages = new JsonArray();
        if (!string.IsNullOrWhiteSpace(request.System))
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.System });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.Prompt });

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = false,
        };
        if (request.Temperature is { } temperature)
        {
            body["temperature"] = temperature;
        }

        if (request.MaxTokens is { } maxTokens)
        {
            body["max_tokens"] = maxTokens;
        }

        return body.ToJsonString();
    }

    internal static ModelReply ParseReply(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ModelException($"Response was not JSON: {Truncate(json, 200)}", ex);
        }

        var choices = (root as JsonObject)?["choices"] as JsonArray;
        var content = (choices is { Count: > 0 } ? choices[0]?["message"]?["content"]?.GetValue<string>() : null)
            ?? throw new ModelException($"Response had no choices[0].message.content: {Truncate(json, 200)}");

        var usage = (root as JsonObject)?["usage"];
        return new ModelReply(
            ModelText.StripThinking(content),
            usage?["prompt_tokens"]?.GetValue<long>(),
            usage?["completion_tokens"]?.GetValue<long>());
    }

    private void AddAuth(HttpRequestMessage message)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return;
        }

        if (_azureKeyHeader)
        {
            message.Headers.Add("api-key", _apiKey);
        }
        else
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
