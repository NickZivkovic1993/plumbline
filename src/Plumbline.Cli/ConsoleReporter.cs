using Plumbline.Reporting;
using Plumbline.Running;

namespace Plumbline.Cli;

/// <summary>Human-readable progress and summary. Respects NO_COLOR.</summary>
public sealed class ConsoleReporter(TextWriter output, bool verbose)
{
    private readonly bool _color = Environment.GetEnvironmentVariable("NO_COLOR") is null && !Console.IsOutputRedirected;
    private readonly Lock _lock = new();

    public void CaseCompleted(CaseResult result)
    {
        lock (_lock)
        {
            var passed = result.Samples.Count(s => s.Passed);
            var ms = result.Samples.Count == 0 ? 0 : result.Samples.Max(s => s.LatencyMs);
            Write(result.Passed ? "  PASS " : "  FAIL ", result.Passed ? ConsoleColor.Green : ConsoleColor.Red);
            output.WriteLine($" {result.Id}  ({passed}/{result.Samples.Count}, {ms / 1000.0:0.0}s)");

            if (!result.Passed || verbose)
            {
                foreach (var sample in result.Samples)
                {
                    WriteSample(sample, showPassing: verbose);
                }
            }
        }
    }

    public void Summary(RunResult run, BaselineComparison? comparison)
    {
        output.WriteLine();
        Write($"{run.PassedCount}/{run.Total} passed", run.FailedCount == 0 ? ConsoleColor.Green : ConsoleColor.Red);
        output.WriteLine($" on {run.Model} in {run.DurationMs / 1000.0:0.0}s");

        if (comparison is null)
        {
            return;
        }

        var regressions = comparison.Regressions.ToList();
        output.WriteLine($"Baseline: {regressions.Count} regressed, {comparison.Fixes.Count()} fixed, " +
                         $"{comparison.Cases.Count(c => c.Change == CaseChange.StillFailing)} still failing, " +
                         $"{comparison.Cases.Count(c => c.Change == CaseChange.New)} new");
        foreach (var regression in regressions)
        {
            Write("  REGRESSED ", ConsoleColor.Red);
            output.WriteLine($"{regression.Id}: {regression.Current.FirstFailure}");
        }
    }

    private void WriteSample(SampleResult sample, bool showPassing)
    {
        if (sample.Error is not null)
        {
            output.WriteLine($"      sample {sample.Index}: error: {sample.Error}");
            return;
        }

        foreach (var assertion in sample.Assertions.Where(a => showPassing || !a.Passed))
        {
            output.Write($"      sample {sample.Index}: ");
            Write(assertion.Passed ? "ok  " : "fail", assertion.Passed ? ConsoleColor.DarkGreen : ConsoleColor.Red);
            output.WriteLine($" {assertion.Name}{(assertion.Message is null ? "" : $" ({assertion.Message})")}");
        }

        if (sample.Output is not null)
        {
            var preview = sample.Output.ReplaceLineEndings(" ");
            output.WriteLine($"      answer: {(preview.Length > 200 ? preview[..200] + "…" : preview)}");
        }
    }

    private void Write(string text, ConsoleColor color)
    {
        if (!_color)
        {
            output.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        output.Write(text);
        Console.ForegroundColor = previous;
    }
}
