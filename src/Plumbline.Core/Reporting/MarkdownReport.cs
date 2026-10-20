using System.Globalization;
using System.Text;
using Plumbline.Running;

namespace Plumbline.Reporting;

/// <summary>A compact Markdown summary, sized for a GitHub job summary or a PR comment.</summary>
public static class MarkdownReport
{
    public static string Build(RunResult run, BaselineComparison? comparison = null)
    {
        var md = new StringBuilder();
        var icon = run.FailedCount == 0 ? "✅" : "❌";
        md.AppendLine(CultureInfo.InvariantCulture, $"### {icon} Plumbline: {run.Suite}");
        md.AppendLine();
        md.Append(CultureInfo.InvariantCulture, $"**{run.PassedCount}/{run.Total}** cases passed on `{run.Model}` in {run.DurationMs / 1000.0:0.0}s");
        if (comparison is not null)
        {
            md.Append(CultureInfo.InvariantCulture, $" · **{comparison.Regressions.Count()}** regressed · **{comparison.Fixes.Count()}** fixed vs baseline");
        }

        md.AppendLine();
        md.AppendLine();
        md.AppendLine("| Case | Result | Pass rate | First failure |");
        md.AppendLine("| --- | --- | --- | --- |");

        var changes = comparison?.Cases.ToDictionary(c => c.Id, c => c.Change);
        foreach (var result in run.Cases.OrderBy(c => c.Passed).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            var change = changes?.GetValueOrDefault(result.Id);
            var status = (result.Passed, change) switch
            {
                (false, CaseChange.Regressed) => "🔻 regressed",
                (false, _) => "❌ fail",
                (true, CaseChange.Fixed) => "🟢 fixed",
                _ => "✅ pass",
            };
            var samples = result.Samples.Count;
            var passed = result.Samples.Count(s => s.Passed);
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| `{result.Id}` | {status} | {passed}/{samples} | {Escape(result.Passed ? "" : result.FirstFailure ?? "")} |");
        }

        if (comparison?.Removed is { Count: > 0 } removed)
        {
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"Removed since baseline: {string.Join(", ", removed.Select(r => $"`{r}`"))}");
        }

        return md.ToString();
    }

    private static string Escape(string text)
    {
        var oneLine = text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
        return oneLine.Length <= 140 ? oneLine : oneLine[..140] + "…";
    }
}
