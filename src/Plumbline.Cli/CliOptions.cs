using System.Globalization;

namespace Plumbline.Cli;

public enum FailOn
{
    /// <summary>Exit non-zero if any case fails.</summary>
    Any,

    /// <summary>Exit non-zero only for cases that pass in the baseline and fail now (plus new failing cases).</summary>
    Regressions,
}

public sealed record CliOptions
{
    public required string Command { get; init; }
    public string? SuitePath { get; init; }
    public string? Replay { get; init; }
    public string? Record { get; init; }
    public string? Baseline { get; init; }
    public string? Out { get; init; }
    public string? JUnit { get; init; }
    public string? Markdown { get; init; }
    public string? Model { get; init; }
    public string? BaseUrl { get; init; }
    public string? Filter { get; init; }
    public int Parallel { get; init; } = 4;
    public FailOn FailOn { get; init; } = FailOn.Any;
    public bool Verbose { get; init; }

    public const string Usage =
        """
        plumbline: regression tests for LLM features

        Usage:
          plumbline run <suite.yaml> [options]     Run a suite and grade every answer
          plumbline validate <suite.yaml>          Check a suite file without calling a model
          plumbline --version | --help

        Options for run:
          --replay <cassette.json>    Answer from a recorded cassette (no network, no keys)
          --record <cassette.json>    Call the live model and record every answer
          --baseline <results.json>   Compare against a previous run's results
          --fail-on any|regressions   Exit 1 on any failure (default) or only on regressions
          --out <results.json>        Save full results (use as the next baseline)
          --junit <report.xml>        Write a JUnit XML report
          --markdown <summary.md>     Write a Markdown summary (also appended to
                                      $GITHUB_STEP_SUMMARY when running in GitHub Actions)
          --model <name>              Override the suite's model name
          --base-url <url>            Override the suite's model endpoint
          --filter <text>             Only run cases whose id contains <text>
          --parallel <n>              Concurrent model calls (default 4)
          -v, --verbose               Print every answer, not just failures

        Exit codes: 0 passed, 1 failed or regressed, 2 invalid input.
        """;

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
        {
            return new CliOptions { Command = "help" };
        }

        if (args[0] is "--version" or "version")
        {
            return new CliOptions { Command = "version" };
        }

        var command = args[0];
        if (command is not ("run" or "validate"))
        {
            throw new CliException($"Unknown command '{command}'. Run 'plumbline --help'.");
        }

        var options = new CliOptions { Command = command };
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-'))
            {
                options = options.SuitePath is null
                    ? options with { SuitePath = arg }
                    : throw new CliException($"Unexpected argument '{arg}'.");
                continue;
            }

            string Value() => i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : throw new CliException($"{arg} needs a value.");

            options = arg switch
            {
                "--replay" => options with { Replay = Value() },
                "--record" => options with { Record = Value() },
                "--baseline" => options with { Baseline = Value() },
                "--out" => options with { Out = Value() },
                "--junit" => options with { JUnit = Value() },
                "--markdown" => options with { Markdown = Value() },
                "--model" => options with { Model = Value() },
                "--base-url" => options with { BaseUrl = Value() },
                "--filter" => options with { Filter = Value() },
                "--parallel" => options with { Parallel = ParsePositive(arg, Value()) },
                "--fail-on" => options with { FailOn = ParseFailOn(Value()) },
                "-v" or "--verbose" => options with { Verbose = true },
                _ => throw new CliException($"Unknown option '{arg}'. Run 'plumbline --help'."),
            };
        }

        if (options.SuitePath is null)
        {
            throw new CliException($"'{command}' needs a suite file, e.g. plumbline {command} evals/suite.yaml");
        }

        if (options.Replay is not null && options.Record is not null)
        {
            throw new CliException("--replay and --record cannot be used together.");
        }

        if (options.FailOn == FailOn.Regressions && options.Baseline is null)
        {
            throw new CliException("--fail-on regressions needs --baseline.");
        }

        return options;
    }

    private static int ParsePositive(string name, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : throw new CliException($"{name} must be a positive number, got '{value}'.");

    private static FailOn ParseFailOn(string value) => value.ToLowerInvariant() switch
    {
        "any" => FailOn.Any,
        "regressions" => FailOn.Regressions,
        _ => throw new CliException($"--fail-on must be 'any' or 'regressions', got '{value}'."),
    };
}

public sealed class CliException(string message) : Exception(message);
