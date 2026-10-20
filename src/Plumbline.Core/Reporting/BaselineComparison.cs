using Plumbline.Running;

namespace Plumbline.Reporting;

public enum CaseChange
{
    /// <summary>Passed in the baseline and still passes.</summary>
    Passing,

    /// <summary>Passed in the baseline, fails now. This is what blocks a PR.</summary>
    Regressed,

    /// <summary>Failed in the baseline, passes now.</summary>
    Fixed,

    /// <summary>Failed in the baseline and still fails: known, not new.</summary>
    StillFailing,

    /// <summary>Not in the baseline.</summary>
    New,
}

public sealed record CaseComparison(string Id, CaseChange Change, CaseResult Current);

/// <summary>Diffs a run against a previous run, case by case.</summary>
public sealed record BaselineComparison(IReadOnlyList<CaseComparison> Cases, IReadOnlyList<string> Removed)
{
    public IEnumerable<CaseComparison> Regressions => Cases.Where(c => c.Change == CaseChange.Regressed);

    public IEnumerable<CaseComparison> Fixes => Cases.Where(c => c.Change == CaseChange.Fixed);

    /// <summary>Cases that fail now and were not already known to fail: regressions plus new failing cases.</summary>
    public IEnumerable<CaseComparison> NewFailures =>
        Cases.Where(c => c.Change == CaseChange.Regressed || (c.Change == CaseChange.New && !c.Current.Passed));

    public static BaselineComparison Compare(RunResult current, RunResult baseline)
    {
        var before = baseline.Cases.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var cases = current.Cases
            .Select(c => new CaseComparison(c.Id, Classify(c, before.GetValueOrDefault(c.Id)), c))
            .ToList();

        var currentIds = current.Cases.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = baseline.Cases.Select(c => c.Id).Where(id => !currentIds.Contains(id)).ToList();
        return new BaselineComparison(cases, removed);
    }

    private static CaseChange Classify(CaseResult now, CaseResult? then) => (then?.Passed, now.Passed) switch
    {
        (null, _) => CaseChange.New,
        (true, true) => CaseChange.Passing,
        (true, false) => CaseChange.Regressed,
        (false, true) => CaseChange.Fixed,
        (false, false) => CaseChange.StillFailing,
    };
}
