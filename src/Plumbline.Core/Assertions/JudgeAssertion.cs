using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Plumbline.Models;

namespace Plumbline.Assertions;

/// <summary>
/// LLM-as-judge: a second model scores the answer 1-5 against a plain-language criterion.
/// The judge runs at temperature 0 and its replies go through the cassette like any other call.
/// </summary>
public sealed partial class JudgeAssertion(string criterion, int minScore = 4) : IAssertion
{
    internal const string JudgeSystemPrompt =
        """
        You are a strict evaluator of AI assistant answers.
        Score how well the RESPONSE satisfies the CRITERION, from 1 to 5:
        5 = fully satisfies it, 4 = satisfies it with minor issues, 3 = partially,
        2 = mostly fails, 1 = clearly fails or contradicts it.
        Judge only the criterion, not general quality.
        Reply with JSON only, no prose: {"score": <1-5>, "reason": "<one short sentence>"}
        """;

    public string Criterion => criterion;

    public int MinScore => minScore;

    public string Name => $"judge >= {minScore}: {criterion}";

    public async ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default)
    {
        if (context.Judge is null || context.JudgeSettings is null)
        {
            return AssertionResult.Fail(Name, "no judge model configured (add a 'judge:' section to the suite)");
        }

        var request = new ModelRequest(
            context.JudgeSettings.Name,
            JudgeSystemPrompt,
            BuildPrompt(criterion, context.Prompt, context.Output),
            Temperature: 0,
            MaxTokens: context.JudgeSettings.MaxTokens,
            Label: $"{context.Label}/judge");

        ModelReply reply;
        try
        {
            reply = await context.Judge.CompleteAsync(request, cancellationToken);
        }
        catch (ModelException ex)
        {
            return AssertionResult.Fail(Name, $"judge call failed: {ex.Message}");
        }

        if (!TryParseVerdict(reply.Text, out var score, out var reason))
        {
            return AssertionResult.Fail(Name, $"could not read a score from the judge: \"{EqualsAssertion.Preview(reply.Text)}\"");
        }

        var message = $"score {score}/5: {reason}";
        return score >= minScore
            ? AssertionResult.Pass(Name, message, score)
            : AssertionResult.Fail(Name, message, score);
    }

    internal static string BuildPrompt(string criterion, string prompt, string output) =>
        $"""
        CRITERION:
        {criterion}

        USER PROMPT:
        {prompt}

        RESPONSE:
        {output}
        """;

    internal static bool TryParseVerdict(string text, out int score, out string reason)
    {
        score = 0;
        reason = string.Empty;
        var json = ModelText.ExtractJson(ModelText.StripThinking(text));

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("score", out var scoreElement))
            {
                var value = scoreElement.ValueKind == JsonValueKind.String
                    ? double.Parse(scoreElement.GetString()!, CultureInfo.InvariantCulture)
                    : scoreElement.GetDouble();
                score = Clamp(value);
                reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : string.Empty;
                return true;
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // Fall through to the lenient pattern below; small models often wrap or mangle the JSON.
        }

        var match = LooseScore().Match(text);
        if (match.Success)
        {
            score = Clamp(double.Parse(match.Groups["score"].Value, CultureInfo.InvariantCulture));
            reason = "(free-text verdict)";
            return true;
        }

        return false;
    }

    private static int Clamp(double value) => (int)Math.Clamp(Math.Round(value), 1, 5);

    [GeneratedRegex(@"score\W{0,4}(?<score>[1-5])\b", RegexOptions.IgnoreCase)]
    private static partial Regex LooseScore();
}
