using Plumbline.Assertions;
using Plumbline.Reporting;
using Plumbline.Running;

namespace Plumbline.Tests;

public class ReportingTests
{
    private static CaseResult Case(string id, bool passed, string? failure = null) => new(
        id,
        passed,
        passed ? 1 : 0,
        1,
        [new SampleResult(0, $"answer for {id}", null, 1200, 10, 5,
            [passed ? AssertionResult.Pass("contains \"x\"") : AssertionResult.Fail("contains \"x\"", failure ?? "missing x")])]);

    private static RunResult Run(params CaseResult[] cases) =>
        new("demo", "qwen2.5:7b", new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), 4200, cases);

    [Fact]
    public void Classifies_changes_against_the_baseline()
    {
        var baseline = Run(Case("stays-green", true), Case("breaks", true), Case("gets-fixed", false), Case("known-bad", false), Case("deleted", true));
        var current = Run(Case("stays-green", true), Case("breaks", false), Case("gets-fixed", true), Case("known-bad", false), Case("brand-new", false));

        var diff = BaselineComparison.Compare(current, baseline);

        Assert.Equal(CaseChange.Passing, diff.Cases.Single(c => c.Id == "stays-green").Change);
        Assert.Equal("breaks", Assert.Single(diff.Regressions).Id);
        Assert.Equal("gets-fixed", Assert.Single(diff.Fixes).Id);
        Assert.Equal(CaseChange.StillFailing, diff.Cases.Single(c => c.Id == "known-bad").Change);
        Assert.Equal(CaseChange.New, diff.Cases.Single(c => c.Id == "brand-new").Change);
        Assert.Equal(["deleted"], diff.Removed);
        Assert.Equal(["breaks", "brand-new"], diff.NewFailures.Select(c => c.Id));
    }

    [Fact]
    public void JUnit_report_counts_failures_and_explains_them()
    {
        var xml = JUnitReport.Build(Run(Case("ok", true), Case("bad", false, "answer does not contain \"x\""))).ToString();

        Assert.Contains("tests=\"2\"", xml);
        Assert.Contains("failures=\"1\"", xml);
        Assert.Contains("<testcase classname=\"demo\" name=\"bad\"", xml);
        Assert.Contains("answer does not contain", xml);
        Assert.Contains("<system-out>answer for ok</system-out>", xml);
        Assert.Contains("name=\"model\" value=\"qwen2.5:7b\"", xml);
    }

    [Fact]
    public void Markdown_summary_puts_failures_first_and_marks_regressions()
    {
        var baseline = Run(Case("a", true), Case("b", true));
        var current = Run(Case("a", true), Case("b", false, "pipe | in message"));

        var markdown = MarkdownReport.Build(current, BaselineComparison.Compare(current, baseline));

        Assert.Contains("### ❌ Plumbline: demo", markdown);
        Assert.Contains("**1/2** cases passed on `qwen2.5:7b`", markdown);
        Assert.Contains("**1** regressed", markdown);
        Assert.Contains("| `b` | 🔻 regressed |", markdown);
        Assert.Contains("pipe \\| in message", markdown);
        Assert.True(markdown.IndexOf("`b`", StringComparison.Ordinal) < markdown.IndexOf("`a`", StringComparison.Ordinal));
    }

    [Fact]
    public void Results_round_trip_through_json()
    {
        using var temp = new TempDir();
        var path = temp.File("results.json");
        var run = Run(Case("a", true), Case("b", false));

        run.Save(path);
        var loaded = RunResult.Load(path);

        Assert.Equal(run.Suite, loaded.Suite);
        Assert.Equal(["a", "b"], loaded.Cases.Select(c => c.Id));
        Assert.False(loaded.Cases[1].Passed);
        Assert.Equal("missing x", loaded.Cases[1].Samples[0].Assertions[0].Message);
    }
}
