using Plumbline.Assertions;
using Plumbline.Running;

namespace Plumbline.Tests;

public class SuiteRunnerTests
{
    private static readonly ModelSettings Model = new("small", new Uri("http://localhost/v1"), Temperature: 0.7, MaxTokens: 64);

    private static Suite SuiteOf(params EvalCase[] cases) =>
        new("test", null, Model, new ModelSettings("big", Model.BaseUrl), "You are terse.", cases);

    [Fact]
    public async Task Grades_each_case_and_passes_settings_to_the_model()
    {
        var model = new FakeModel(r => r.Prompt == "refund?" ? "Refunds within 30 days." : "No idea.");
        var suite = SuiteOf(
            new EvalCase("refund", "refund?", [new ContainsAssertion("30 days")]),
            new EvalCase("shipping", "shipping?", [new ContainsAssertion("€4.95")]));

        var run = await new SuiteRunner(model).RunAsync(suite);

        Assert.Equal(1, run.PassedCount);
        Assert.True(run.Cases.Single(c => c.Id == "refund").Passed);
        var failed = run.Cases.Single(c => c.Id == "shipping");
        Assert.False(failed.Passed);
        Assert.Contains("€4.95", failed.FirstFailure);

        var request = model.Requests.First(r => r.Prompt == "refund?");
        Assert.Equal("small", request.Model);
        Assert.Equal("You are terse.", request.System);
        Assert.Equal(0.7, request.Temperature);
        Assert.Equal(64, request.MaxTokens);
        Assert.Equal("refund", request.Label);
    }

    [Fact]
    public async Task Case_system_prompt_overrides_the_suite()
    {
        var model = new FakeModel("{}");
        await new SuiteRunner(model).RunAsync(SuiteOf(new EvalCase("triage", "msg", [new JsonValidAssertion()], System: "JSON only.")));
        Assert.Equal("JSON only.", Assert.Single(model.Requests).System);
    }

    [Theory]
    [InlineData(0.66, true)]
    [InlineData(1.0, false)]
    public async Task Samples_are_scored_against_the_pass_rate(double passRate, bool expected)
    {
        // Sample 1 of 3 gives a bad answer.
        var model = new FakeModel(r => r.Sample == 1 ? "nope" : "yes");
        var run = await new SuiteRunner(model).RunAsync(SuiteOf(
            new EvalCase("flaky", "q", [new EqualsAssertion("yes")], Samples: 3, PassRate: passRate)));

        var result = Assert.Single(run.Cases);
        Assert.Equal(expected, result.Passed);
        Assert.Equal(0.667, result.PassRate);
        Assert.Equal(["flaky#0", "flaky#1", "flaky#2"], model.Requests.Select(r => r.Label!).Order());
    }

    [Fact]
    public async Task Model_errors_fail_the_sample_instead_of_crashing_the_run()
    {
        var run = await new SuiteRunner(new FailingModel("503 from upstream")).RunAsync(
            SuiteOf(new EvalCase("a", "q", [new ContainsAssertion("x")])));

        var sample = Assert.Single(Assert.Single(run.Cases).Samples);
        Assert.False(sample.Passed);
        Assert.Equal("503 from upstream", sample.Error);
    }

    [Fact]
    public async Task Judge_assertions_use_the_judge_model()
    {
        var model = new FakeModel("Refunds within 30 days.");
        var judge = new FakeModel("""{"score": 5, "reason": "correct"}""");

        var run = await new SuiteRunner(model, judge).RunAsync(
            SuiteOf(new EvalCase("refund", "refund?", [new JudgeAssertion("Mentions 30 days.")])));

        Assert.True(Assert.Single(run.Cases).Passed);
        Assert.Equal("big", Assert.Single(judge.Requests).Model);
        Assert.Single(model.Requests);
    }

    [Fact]
    public async Task Filter_and_progress_callback()
    {
        var completed = new System.Collections.Concurrent.ConcurrentBag<string>();
        var options = new SuiteRunnerOptions { Filter = "REF", OnCaseCompleted = c => completed.Add(c.Id) };

        var run = await new SuiteRunner(new FakeModel("ok"), options: options).RunAsync(SuiteOf(
            new EvalCase("refund-1", "a", [new ContainsAssertion("ok")]),
            new EvalCase("refund-2", "b", [new ContainsAssertion("ok")]),
            new EvalCase("shipping", "c", [new ContainsAssertion("ok")])));

        Assert.Equal(["refund-1", "refund-2"], run.Cases.Select(c => c.Id));
        Assert.Equal(["refund-1", "refund-2"], completed.Order());
    }

    [Fact]
    public async Task Respects_the_parallelism_limit()
    {
        var gate = new ConcurrencyProbe();
        var suite = SuiteOf([.. Enumerable.Range(0, 12).Select(i => new EvalCase($"c{i}", $"p{i}", [new ContainsAssertion("ok")]))]);

        await new SuiteRunner(gate, options: new SuiteRunnerOptions { Parallelism = 3 }).RunAsync(suite);

        Assert.InRange(gate.MaxInFlight, 1, 3);
    }

    private sealed class ConcurrencyProbe : Models.IChatModel
    {
        private int _inFlight;

        public int MaxInFlight { get; private set; }

        public async Task<Models.ModelReply> CompleteAsync(Models.ModelRequest request, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            lock (this)
            {
                MaxInFlight = Math.Max(MaxInFlight, now);
            }

            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _inFlight);
            return new Models.ModelReply("ok");
        }
    }
}
