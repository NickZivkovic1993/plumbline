using System.Text.Json;
using System.Text.Json.Serialization;
using Plumbline.Assertions;

namespace Plumbline.Running;

/// <summary>Everything one run produced. Saved as JSON, it doubles as the baseline for the next run.</summary>
public sealed record RunResult(
    string Suite,
    string Model,
    DateTimeOffset StartedAt,
    long DurationMs,
    IReadOnlyList<CaseResult> Cases)
{
    public int Total => Cases.Count;

    public int PassedCount => Cases.Count(c => c.Passed);

    public int FailedCount => Total - PassedCount;

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Indented) + Environment.NewLine);
    }

    public static RunResult Load(string path) =>
        JsonSerializer.Deserialize<RunResult>(File.ReadAllText(path), JsonOptions.Default)
        ?? throw new InvalidDataException($"{path} is not a Plumbline result file.");
}

public sealed record CaseResult(
    string Id,
    bool Passed,
    double PassRate,
    double RequiredPassRate,
    IReadOnlyList<SampleResult> Samples)
{
    /// <summary>The first failing check across samples, for one-line reports.</summary>
    [JsonIgnore]
    public string? FirstFailure
    {
        get
        {
            foreach (var sample in Samples)
            {
                if (sample.Error is not null)
                {
                    return sample.Error;
                }

                if (sample.Assertions.FirstOrDefault(a => !a.Passed) is { } failed)
                {
                    return $"{failed.Name}: {failed.Message}";
                }
            }

            return null;
        }
    }
}

public sealed record SampleResult(
    int Index,
    string? Output,
    string? Error,
    long LatencyMs,
    long? InputTokens,
    long? OutputTokens,
    IReadOnlyList<AssertionResult> Assertions)
{
    public bool Passed => Error is null && Assertions.All(a => a.Passed);
}
