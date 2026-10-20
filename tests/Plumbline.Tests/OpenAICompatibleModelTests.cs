using System.Net;
using System.Text.Json;
using Plumbline.Models;

namespace Plumbline.Tests;

public class OpenAICompatibleModelTests
{
    private const string Ok =
        """
        {"choices":[{"message":{"role":"assistant","content":"<think>hmm</think>Refunds within 30 days."}}],
         "usage":{"prompt_tokens":42,"completion_tokens":7}}
        """;

    private static readonly ModelRequest Request = new("qwen2.5:7b", "Be brief.", "Refund?", 0.2, 50);

    [Fact]
    public async Task Posts_chat_completions_and_parses_the_reply()
    {
        var handler = new StubHandler((HttpStatusCode.OK, Ok));
        var model = new OpenAICompatibleModel(new HttpClient(handler), new Uri("http://localhost:11434/v1/"), "secret");

        var reply = await model.CompleteAsync(Request);

        Assert.Equal("Refunds within 30 days.", reply.Text);
        Assert.Equal(42, reply.InputTokens);
        Assert.Equal(7, reply.OutputTokens);

        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("secret", request.Headers.Authorization?.Parameter);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("qwen2.5:7b", root.GetProperty("model").GetString());
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble());
        Assert.Equal(50, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Refund?", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Uses_the_api_key_header_for_azure()
    {
        var handler = new StubHandler((HttpStatusCode.OK, Ok));
        var model = new OpenAICompatibleModel(new HttpClient(handler), new Uri("https://contoso.openai.azure.com/openai/v1"), "azure-key");

        await model.CompleteAsync(Request);

        var request = handler.Seen[0].Request;
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("azure-key", request.Headers.GetValues("api-key").Single());
    }

    [Fact]
    public async Task Omits_optional_fields_and_auth_when_not_set()
    {
        var handler = new StubHandler((HttpStatusCode.OK, Ok));
        var model = new OpenAICompatibleModel(new HttpClient(handler), new Uri("http://localhost:11434/v1"));

        await model.CompleteAsync(new ModelRequest("m", null, "hi"));

        var (request, body) = handler.Seen[0];
        Assert.Null(request.Headers.Authorization);
        using var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.TryGetProperty("temperature", out _));
        Assert.False(json.RootElement.TryGetProperty("max_tokens", out _));
        Assert.Equal(1, json.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task Retries_rate_limits_and_server_errors()
    {
        var handler = new StubHandler(
            (HttpStatusCode.TooManyRequests, "slow down"),
            (HttpStatusCode.BadGateway, "oops"),
            (HttpStatusCode.OK, Ok));
        var model = new OpenAICompatibleModel(new HttpClient(handler), new Uri("http://x/v1"), retryDelay: TimeSpan.Zero);

        var reply = await model.CompleteAsync(Request);

        Assert.Equal("Refunds within 30 days.", reply.Text);
        Assert.Equal(3, handler.Seen.Count);
    }

    [Fact]
    public async Task Gives_up_with_the_status_and_body()
    {
        var handler = new StubHandler((HttpStatusCode.TooManyRequests, "quota exceeded"));
        var model = new OpenAICompatibleModel(new HttpClient(handler), new Uri("http://x/v1"), maxRetries: 1, retryDelay: TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<ModelException>(() => model.CompleteAsync(Request));

        Assert.Contains("429", ex.Message);
        Assert.Contains("quota exceeded", ex.Message);
        Assert.Equal(2, handler.Seen.Count);
    }

    [Fact]
    public async Task Does_not_retry_client_errors()
    {
        var handler = new StubHandler((HttpStatusCode.NotFound, "model 'nope' not found"));
        var model = new OpenAICompatibleModel(new HttpClient(handler), new Uri("http://x/v1"), retryDelay: TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<ModelException>(() => model.CompleteAsync(Request));

        Assert.Contains("model 'nope' not found", ex.Message);
        Assert.Single(handler.Seen);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"choices": []}""")]
    public void Rejects_malformed_responses(string body) =>
        Assert.Throws<ModelException>(() => OpenAICompatibleModel.ParseReply(body));
}
