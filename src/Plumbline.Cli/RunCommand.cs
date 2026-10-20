using Plumbline.Models;
using Plumbline.Reporting;
using Plumbline.Running;

namespace Plumbline.Cli;

/// <summary>plumbline run: load, pick a model source (live, record, replay), run, report, decide the exit code.</summary>
/// <param name="output">Where progress and the summary go.</param>
/// <param name="httpFactory">Creates the HTTP client for live calls; never invoked when replaying.</param>
/// <param name="stepSummaryPath">GitHub's $GITHUB_STEP_SUMMARY file, appended to when set.</param>
public sealed class RunCommand(TextWriter output, Func<HttpClient>? httpFactory = null, string? stepSummaryPath = null)
{
    public async Task<int> ExecuteAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var suite = ApplyOverrides(SuiteLoader.LoadFile(options.SuitePath!), options);
        if (options.Filter is not null)
        {
            suite = suite with { Cases = [.. suite.Cases.Where(c => Matches(c.Id, options.Filter))] };
            if (suite.Cases.Count == 0)
            {
                throw new CliException($"--filter '{options.Filter}' matched no cases.");
            }
        }

        var cassette = options.Replay is not null ? Cassette.Load(options.Replay)
            : options.Record is not null ? new Cassette()
            : null;

        using var http = options.Replay is null
            ? httpFactory?.Invoke() ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) }
            : null;
        var model = Select(suite.Model, http, options, cassette);
        var judge = suite.Judge is null ? null : Select(suite.Judge, http, options, cassette);

        var mode = options.Replay is not null ? $"replaying {options.Replay}"
            : options.Record is not null ? $"recording to {options.Record}"
            : $"live, {suite.Model.BaseUrl}";
        output.WriteLine($"{suite.Name}: {suite.Cases.Count} cases on {suite.Model.Name} ({mode})");
        output.WriteLine();

        var reporter = new ConsoleReporter(output, options.Verbose);
        var runner = new SuiteRunner(model, judge, new SuiteRunnerOptions
        {
            Parallelism = options.Parallel,
            OnCaseCompleted = reporter.CaseCompleted,
        });

        var run = await runner.RunAsync(suite, cancellationToken);

        if (options.Record is not null && cassette is not null)
        {
            cassette.Save(options.Record);
            output.WriteLine($"Recorded {cassette.Count} answers to {options.Record}");
        }

        BaselineComparison? comparison = null;
        if (options.Baseline is not null)
        {
            // With --filter, compare like with like: cases outside the filter were not "removed".
            var baseline = RunResult.Load(options.Baseline);
            baseline = baseline with { Cases = [.. baseline.Cases.Where(c => Matches(c.Id, options.Filter))] };
            comparison = BaselineComparison.Compare(run, baseline);
        }

        reporter.Summary(run, comparison);
        WriteReports(run, comparison, options);

        var failed = options.FailOn == FailOn.Regressions
            ? comparison!.NewFailures.Any()
            : run.FailedCount > 0;
        return failed ? 1 : 0;
    }

    private static bool Matches(string id, string? filter) =>
        filter is null || id.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static IChatModel Select(ModelSettings settings, HttpClient? http, CliOptions options, Cassette? cassette)
    {
        if (options.Replay is not null)
        {
            return new ReplayModel(cassette!);
        }

        var live = new OpenAICompatibleModel(http!, settings.BaseUrl, settings.ResolveApiKey());
        return options.Record is not null ? new RecordingModel(live, cassette!) : live;
    }

    private static Suite ApplyOverrides(Suite suite, CliOptions options)
    {
        if (options.Model is null && options.BaseUrl is null)
        {
            return suite;
        }

        var baseUrl = suite.Model.BaseUrl;
        if (options.BaseUrl is not null && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out baseUrl))
        {
            throw new CliException($"--base-url must be an absolute URL, got '{options.BaseUrl}'.");
        }

        return suite with { Model = suite.Model with { Name = options.Model ?? suite.Model.Name, BaseUrl = baseUrl! } };
    }

    private void WriteReports(RunResult run, BaselineComparison? comparison, CliOptions options)
    {
        if (options.Out is not null)
        {
            run.Save(options.Out);
            output.WriteLine($"Results: {options.Out}");
        }

        if (options.JUnit is not null)
        {
            JUnitReport.Write(run, options.JUnit);
            output.WriteLine($"JUnit:   {options.JUnit}");
        }

        if (options.Markdown is null && string.IsNullOrEmpty(stepSummaryPath))
        {
            return;
        }

        var markdown = MarkdownReport.Build(run, comparison);
        if (options.Markdown is not null)
        {
            File.WriteAllText(options.Markdown, markdown);
            output.WriteLine($"Summary: {options.Markdown}");
        }

        if (!string.IsNullOrEmpty(stepSummaryPath))
        {
            File.AppendAllText(stepSummaryPath, markdown + Environment.NewLine);
        }
    }
}
