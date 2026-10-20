using Plumbline.Assertions;

namespace Plumbline.Tests;

public class JudgeAssertionTests
{
    private static readonly ModelSettings JudgeSettings = new("judge-model", new Uri("http://localhost:11434/v1"), MaxTokens: 100);

    private static Task<AssertionResult> Judge(string judgeReply, int minScore = 4)
    {
        var judge = new FakeModel(judgeReply);
        var context = new AssertionContext("Can I get a refund?", "Yes, within 30 days.", judge, JudgeSettings, "refund");
        return new JudgeAssertion("Confirms the 30-day refund window.", minScore).EvaluateAsync(context).AsTask();
    }

    [Fact]
    public async Task Passes_when_score_meets_threshold()
    {
        var result = await Judge("""{"score": 5, "reason": "States the window."}""");
        Assert.True(result.Passed);
        Assert.Equal(5, result.Score);
        Assert.Contains("States the window.", result.Message);
    }

    [Fact]
    public async Task Fails_below_threshold_and_keeps_the_reason()
    {
        var result = await Judge("""{"score": 2, "reason": "Never mentions 30 days."}""");
        Assert.False(result.Passed);
        Assert.Equal(2, result.Score);
        Assert.Contains("Never mentions 30 days.", result.Message);
    }

    [Fact]
    public async Task Fails_clearly_when_the_judge_reply_has_no_score()
    {
        var result = await Judge("I think it's pretty good overall.");
        Assert.False(result.Passed);
        Assert.Contains("could not read a score", result.Message);
    }

    [Fact]
    public async Task Fails_without_a_judge_model()
    {
        var context = new AssertionContext("p", "o");
        var result = await new JudgeAssertion("anything").EvaluateAsync(context);
        Assert.False(result.Passed);
        Assert.Contains("no judge model", result.Message);
    }

    [Fact]
    public async Task Sends_criterion_prompt_and_answer_at_temperature_zero()
    {
        var judge = new FakeModel("""{"score": 4, "reason": "ok"}""");
        var context = new AssertionContext("Can I get a refund?", "Yes, within 30 days.", judge, JudgeSettings, "refund");
        await new JudgeAssertion("Confirms the window.").EvaluateAsync(context);

        var request = Assert.Single(judge.Requests);
        Assert.Equal("judge-model", request.Model);
        Assert.Equal(0, request.Temperature);
        Assert.Equal(JudgeAssertion.JudgeSystemPrompt, request.System);
        Assert.Contains("Confirms the window.", request.Prompt);
        Assert.Contains("Can I get a refund?", request.Prompt);
        Assert.Contains("Yes, within 30 days.", request.Prompt);
        Assert.Equal("refund/judge", request.Label);
    }

    [Theory]
    [InlineData("""{"score": 4, "reason": "fine"}""", 4)]
    [InlineData("""{"score": "3", "reason": "string score"}""", 3)]
    [InlineData("""{"score": 4.6}""", 5)]
    [InlineData("""{"score": 9}""", 5)]
    [InlineData("<think>The answer mentions 30 days, so 5.</think>{\"score\": 5, \"reason\": \"ok\"}", 5)]
    [InlineData("```json\n{\"score\": 2, \"reason\": \"no\"}\n```", 2)]
    [InlineData("Score: 3 - partially satisfies it", 3)]
    public void Parses_the_verdicts_small_models_actually_produce(string reply, int expected)
    {
        Assert.True(JudgeAssertion.TryParseVerdict(reply, out var score, out _));
        Assert.Equal(expected, score);
    }
}
