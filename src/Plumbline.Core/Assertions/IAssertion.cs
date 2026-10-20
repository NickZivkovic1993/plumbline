using Plumbline.Models;

namespace Plumbline.Assertions;

/// <summary>What an assertion gets to look at: the prompt, the answer, and the judge if one is configured.</summary>
public sealed record AssertionContext(
    string Prompt,
    string Output,
    IChatModel? Judge = null,
    ModelSettings? JudgeSettings = null,
    string? Label = null);

public sealed record AssertionResult(string Name, bool Passed, string? Message = null, double? Score = null)
{
    public static AssertionResult Pass(string name, string? message = null, double? score = null) => new(name, true, message, score);

    public static AssertionResult Fail(string name, string message, double? score = null) => new(name, false, message, score);
}

public interface IAssertion
{
    /// <summary>Short human-readable description, e.g. <c>contains "30 days"</c>.</summary>
    string Name { get; }

    ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default);
}
