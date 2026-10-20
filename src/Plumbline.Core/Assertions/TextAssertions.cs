using System.Text.RegularExpressions;

namespace Plumbline.Assertions;

public sealed class ContainsAssertion(string expected, bool ignoreCase = true) : IAssertion
{
    public string Name => $"contains \"{expected}\"";

    public ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(context.Output.Contains(expected, Comparison(ignoreCase))
            ? AssertionResult.Pass(Name)
            : AssertionResult.Fail(Name, $"answer does not contain \"{expected}\""));

    internal static StringComparison Comparison(bool ignoreCase) =>
        ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

public sealed class NotContainsAssertion(string forbidden, bool ignoreCase = true) : IAssertion
{
    public string Name => $"does not contain \"{forbidden}\"";

    public ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(context.Output.Contains(forbidden, ContainsAssertion.Comparison(ignoreCase))
            ? AssertionResult.Fail(Name, $"answer contains forbidden text \"{forbidden}\"")
            : AssertionResult.Pass(Name));
}

public sealed class EqualsAssertion(string expected, bool ignoreCase = false) : IAssertion
{
    public string Name => $"equals \"{expected}\"";

    public ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(string.Equals(context.Output.Trim(), expected.Trim(), ContainsAssertion.Comparison(ignoreCase))
            ? AssertionResult.Pass(Name)
            : AssertionResult.Fail(Name, $"answer was \"{Preview(context.Output)}\""));

    internal static string Preview(string text) => text.Length <= 80 ? text : text[..80] + "…";
}

public sealed class RegexAssertion : IAssertion
{
    private readonly Regex _regex;

    public RegexAssertion(string pattern, bool ignoreCase = false)
    {
        var options = RegexOptions.Multiline | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        _regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
        Name = $"matches /{pattern}/";
    }

    public string Name { get; }

    public ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_regex.IsMatch(context.Output)
            ? AssertionResult.Pass(Name)
            : AssertionResult.Fail(Name, "answer does not match the pattern"));
}

public sealed class LengthAssertion(int? min, int? max) : IAssertion
{
    public string Name => (min, max) switch
    {
        ({ } lo, { } hi) => $"length {lo}-{hi}",
        ({ } lo, null) => $"length >= {lo}",
        (null, { } hi) => $"length <= {hi}",
        _ => "length",
    };

    public ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default)
    {
        var length = context.Output.Length;
        if (min is { } lo && length < lo)
        {
            return ValueTask.FromResult(AssertionResult.Fail(Name, $"answer is {length} chars, expected at least {lo}"));
        }

        if (max is { } hi && length > hi)
        {
            return ValueTask.FromResult(AssertionResult.Fail(Name, $"answer is {length} chars, expected at most {hi}"));
        }

        return ValueTask.FromResult(AssertionResult.Pass(Name));
    }
}
