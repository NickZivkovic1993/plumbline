using System.Globalization;
using System.Xml.Linq;
using Plumbline.Running;

namespace Plumbline.Reporting;

/// <summary>JUnit XML, the format every CI system (GitHub, Azure DevOps, GitLab, Jenkins) can display.</summary>
public static class JUnitReport
{
    public static XDocument Build(RunResult run)
    {
        var suite = new XElement("testsuite",
            new XAttribute("name", run.Suite),
            new XAttribute("tests", run.Total),
            new XAttribute("failures", run.FailedCount),
            new XAttribute("errors", 0),
            new XAttribute("time", Seconds(run.DurationMs)),
            new XAttribute("timestamp", run.StartedAt.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("properties",
                new XElement("property", new XAttribute("name", "model"), new XAttribute("value", run.Model))));

        foreach (var result in run.Cases)
        {
            var testCase = new XElement("testcase",
                new XAttribute("classname", run.Suite),
                new XAttribute("name", result.Id),
                new XAttribute("time", Seconds(result.Samples.Sum(s => s.LatencyMs))));

            if (!result.Passed)
            {
                testCase.Add(new XElement("failure",
                    new XAttribute("message", $"pass rate {result.PassRate:P0} < required {result.RequiredPassRate:P0}: {result.FirstFailure}"),
                    Details(result)));
            }

            var output = result.Samples.FirstOrDefault()?.Output;
            if (output is not null)
            {
                testCase.Add(new XElement("system-out", output));
            }

            suite.Add(testCase);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("testsuites", suite));
    }

    public static void Write(RunResult run, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Build(run).Save(path);
    }

    private static string Details(CaseResult result) => string.Join(Environment.NewLine, result.Samples.SelectMany(sample =>
        sample.Error is not null
            ? [$"sample {sample.Index}: error: {sample.Error}"]
            : sample.Assertions.Select(a => $"sample {sample.Index}: {(a.Passed ? "PASS" : "FAIL")} {a.Name}{(a.Message is null ? "" : $" ({a.Message})")}")));

    private static string Seconds(long ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
}
