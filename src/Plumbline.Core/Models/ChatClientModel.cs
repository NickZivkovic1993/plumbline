using Microsoft.Extensions.AI;

namespace Plumbline.Models;

/// <summary>
/// Adapts any Microsoft.Extensions.AI <see cref="IChatClient"/> (Azure OpenAI, OpenAI, Ollama,
/// Foundry, Semantic Kernel connectors, your own decorators) so Plumbline can evaluate it.
/// </summary>
public sealed class ChatClientModel(IChatClient client) : IChatModel
{
    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> messages = [];
        if (!string.IsNullOrWhiteSpace(request.System))
        {
            messages.Add(new ChatMessage(ChatRole.System, request.System));
        }

        messages.Add(new ChatMessage(ChatRole.User, request.Prompt));

        var options = new ChatOptions
        {
            ModelId = request.Model,
            Temperature = request.Temperature is { } t ? (float)t : null,
            MaxOutputTokens = request.MaxTokens,
        };

        var response = await client.GetResponseAsync(messages, options, cancellationToken);
        return new ModelReply(
            ModelText.StripThinking(response.Text),
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount);
    }
}
