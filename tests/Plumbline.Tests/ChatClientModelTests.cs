using Microsoft.Extensions.AI;
using Plumbline.Models;

namespace Plumbline.Tests;

public class ChatClientModelTests
{
    [Fact]
    public async Task Adapts_an_IChatClient()
    {
        var client = new CapturingChatClient("<think>…</think>Store credit only.");
        var model = new ChatClientModel(client);

        var reply = await model.CompleteAsync(new ModelRequest("gpt-x", "Policy: ...", "Cash refund after 45 days?", 0.5, 80));

        Assert.Equal("Store credit only.", reply.Text);
        Assert.Equal(3, reply.InputTokens);
        Assert.Equal(4, reply.OutputTokens);
        Assert.Equal([ChatRole.System, ChatRole.User], client.Messages.Select(m => m.Role));
        Assert.Equal("Cash refund after 45 days?", client.Messages[1].Text);
        Assert.Equal("gpt-x", client.Options?.ModelId);
        Assert.Equal(0.5f, client.Options?.Temperature);
        Assert.Equal(80, client.Options?.MaxOutputTokens);
    }

    private sealed class CapturingChatClient(string answer) : IChatClient
    {
        public List<ChatMessage> Messages { get; } = [];

        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
            {
                Usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 4 },
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
