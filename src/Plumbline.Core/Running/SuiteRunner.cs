using System.Diagnostics;
using Plumbline.Assertions;
using Plumbline.Models;

namespace Plumbline.Running;

public sealed class SuiteRunnerOptions
{
    /// <summary>How many model calls may be in flight at once.</summary>
    public int Parallelism { get; init; } = 4;

    /// <summary>Only run cases whose id contains this text (case-insensitive).</summary>
    public string? Filter { get; init; }

    /// <summary>Called as each case finishes, in completion order.</summary>
    public Action<CaseResult>? OnCaseCompleted { get; init; }
}

/// <summary>Runs every case of a suite against a model and grades the answers.</summary>
public sealed class SuiteRunner(IChatModel model, IChatModel? judge = null, SuiteRunnerOptions? options = null)
{
    private readonly SuiteRunnerOptions _options = options ?? new SuiteRunnerOptions();

    public async Task<RunResult> RunAsync(Suite suite, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        using var gate = new SemaphoreSlim(Math.Max(1, _options.Parallelism));

        var cases = suite.Cases
            .Where(c => _options.Filter is null || c.Id.Contains(_options.Filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = await Task.WhenAll(cases.Select(async evalCase =>
        {
            var samples = await Task.WhenAll(Enumerable.Range(0, evalCase.Samples)
                .Select(i => RunSampleAsync(suite, evalCase, i, gate, cancellationToken)));

            var passRate = samples.Count(s => s.Passed) / (double)samples.Length;
            // A small epsilon so "0.66" accepts two out of three.
            var result = new CaseResult(evalCase.Id, passRate + 0.005 >= evalCase.PassRate, Math.Round(passRate, 3), evalCase.PassRate, samples);
            _options.OnCaseCompleted?.Invoke(result);
            return result;
        }));

        return new RunResult(suite.Name, suite.Model.Name, startedAt, clock.ElapsedMilliseconds, results);
    }

    private async Task<SampleResult> RunSampleAsync(Suite suite, EvalCase evalCase, int index, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        var label = evalCase.Samples > 1 ? $"{evalCase.Id}#{index}" : evalCase.Id;
        var request = new ModelRequest(
            suite.Model.Name,
            evalCase.System ?? suite.System,
            evalCase.Prompt,
            suite.Model.Temperature,
            suite.Model.MaxTokens,
            index,
            label);

        ModelReply reply;
        var clock = Stopwatch.StartNew();
        await gate.WaitAsync(cancellationToken);
        try
        {
            reply = await model.CompleteAsync(request, cancellationToken);
        }
        catch (ModelException ex)
        {
            return new SampleResult(index, null, ex.Message, clock.ElapsedMilliseconds, null, null, []);
        }
        finally
        {
            gate.Release();
        }

        var latency = clock.ElapsedMilliseconds;
        var context = new AssertionContext(evalCase.Prompt, reply.Text, judge, suite.Judge, label);
        var graded = new List<AssertionResult>(evalCase.Assertions.Count);
        foreach (var assertion in evalCase.Assertions)
        {
            // Judge calls hit a model too, so they share the concurrency limit.
            var needsModel = assertion is JudgeAssertion;
            if (needsModel)
            {
                await gate.WaitAsync(cancellationToken);
            }

            try
            {
                graded.Add(await assertion.EvaluateAsync(context, cancellationToken));
            }
            finally
            {
                if (needsModel)
                {
                    gate.Release();
                }
            }
        }

        return new SampleResult(index, reply.Text, null, latency, reply.InputTokens, reply.OutputTokens, graded);
    }
}
