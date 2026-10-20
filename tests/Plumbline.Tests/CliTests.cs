using Plumbline.Cli;
using Plumbline.Running;

namespace Plumbline.Tests;

public class CliTests
{
    [Fact]
    public void Parses_a_full_run_command()
    {
        var options = CliOptions.Parse([
            "run", "suite.yaml", "--replay", "c.json", "--baseline", "b.json", "--fail-on", "regressions",
            "--out", "r.json", "--junit", "j.xml", "--markdown", "s.md", "--model", "big", "--base-url", "http://h/v1",
            "--filter", "refund", "--parallel", "8", "-v",
        ]);

        Assert.Equal("run", options.Command);
        Assert.Equal("suite.yaml", options.SuitePath);
        Assert.Equal("c.json", options.Replay);
        Assert.Equal("b.json", options.Baseline);
        Assert.Equal(FailOn.Regressions, options.FailOn);
        Assert.Equal("r.json", options.Out);
        Assert.Equal("j.xml", options.JUnit);
        Assert.Equal("s.md", options.Markdown);
        Assert.Equal("big", options.Model);
        Assert.Equal("http://h/v1", options.BaseUrl);
        Assert.Equal("refund", options.Filter);
        Assert.Equal(8, options.Parallel);
        Assert.True(options.Verbose);
    }

    [Theory]
    [InlineData(new string[0], "help")]
    [InlineData(new[] { "--help" }, "help")]
    [InlineData(new[] { "--version" }, "version")]
    public void Recognises_meta_commands(string[] args, string command) =>
        Assert.Equal(command, CliOptions.Parse(args).Command);

    [Theory]
    [InlineData("deploy suite.yaml", "Unknown command")]
    [InlineData("run", "needs a suite file")]
    [InlineData("run s.yaml --replay a --record b", "cannot be used together")]
    [InlineData("run s.yaml --fail-on regressions", "needs --baseline")]
    [InlineData("run s.yaml --fail-on sometimes", "must be 'any' or 'regressions'")]
    [InlineData("run s.yaml --parallel 0", "positive number")]
    [InlineData("run s.yaml --replay", "needs a value")]
    [InlineData("run s.yaml --frobnicate", "Unknown option")]
    [InlineData("run a.yaml b.yaml", "Unexpected argument")]
    public void Rejects_bad_arguments(string args, string expected) =>
        Assert.Contains(expected, Assert.Throws<CliException>(() => CliOptions.Parse(args.Split(' '))).Message);

    private static readonly string Suite = Repo.Path("examples", "support-bot", "suite.yaml");
    private static readonly string Cassette = Repo.Path("examples", "support-bot", "cassette.json");
    private static readonly string Baseline = Repo.Path("examples", "support-bot", "baseline.json");

    [Fact]
    public async Task Example_suite_replays_offline_and_matches_its_baseline()
    {
        using var temp = new TempDir();
        var output = new StringWriter();
        var options = CliOptions.Parse([
            "run", Suite, "--replay", Cassette, "--baseline", Baseline, "--fail-on", "regressions",
            "--out", temp.File("results.json"), "--junit", temp.File("junit.xml"), "--markdown", temp.File("summary.md"),
        ]);

        var exit = await new RunCommand(output, () => throw new InvalidOperationException("replay must not touch the network"))
            .ExecuteAsync(options, CancellationToken.None);

        Assert.True(exit == 0, output.ToString());
        Assert.Contains("0 regressed", output.ToString());
        Assert.True(File.Exists(temp.File("junit.xml")));
        Assert.Contains("Plumbline: contoso-support", await File.ReadAllTextAsync(temp.File("summary.md")));

        var replayed = RunResult.Load(temp.File("results.json"));
        var recorded = RunResult.Load(Baseline);
        Assert.Equal(
            recorded.Cases.OrderBy(c => c.Id).Select(c => (c.Id, c.Passed)),
            replayed.Cases.OrderBy(c => c.Id).Select(c => (c.Id, c.Passed)));
    }

    [Fact]
    public async Task Fail_on_any_reports_known_failures()
    {
        var recorded = RunResult.Load(Baseline);
        var options = CliOptions.Parse(["run", Suite, "--replay", Cassette]);

        var exit = await new RunCommand(new StringWriter(), () => throw new InvalidOperationException())
            .ExecuteAsync(options, CancellationToken.None);

        Assert.Equal(recorded.FailedCount > 0 ? 1 : 0, exit);
    }

    [Fact]
    public async Task Filtered_runs_compare_only_the_selected_cases()
    {
        using var temp = new TempDir();
        var output = new StringWriter();
        var options = CliOptions.Parse([
            "run", Suite, "--replay", Cassette, "--baseline", Baseline, "--filter", "refund", "--markdown", temp.File("s.md"),
        ]);

        var exit = await new RunCommand(output).ExecuteAsync(options, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("3 cases on qwen2.5:7b", output.ToString());
        Assert.DoesNotContain("Removed since baseline", await File.ReadAllTextAsync(temp.File("s.md")));
    }

    [Fact]
    public async Task Filter_that_matches_nothing_is_an_input_error()
    {
        var options = CliOptions.Parse(["run", Suite, "--replay", Cassette, "--filter", "no-such-case"]);
        await Assert.ThrowsAsync<CliException>(() =>
            new RunCommand(new StringWriter(), () => new HttpClient()).ExecuteAsync(options, CancellationToken.None));
    }
}
