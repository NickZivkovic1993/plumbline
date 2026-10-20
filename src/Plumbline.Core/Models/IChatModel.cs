namespace Plumbline.Models;

/// <summary>A single-turn completion request. <see cref="Label"/> is for humans and never affects caching.</summary>
public sealed record ModelRequest(
    string Model,
    string? System,
    string Prompt,
    double? Temperature = null,
    int? MaxTokens = null,
    int Sample = 0,
    string? Label = null);

public sealed record ModelReply(string Text, long? InputTokens = null, long? OutputTokens = null);

/// <summary>The only thing Plumbline needs from a model: one prompt in, one answer out.</summary>
public interface IChatModel
{
    Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default);
}

public sealed class ModelException(string message, Exception? inner = null) : Exception(message, inner);
