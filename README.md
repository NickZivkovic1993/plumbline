# Plumbline

**Regression tests for LLM features in .NET.** Write down what good answers look like, run them in CI,
and fail the pull request when a prompt change, a model swap or a provider update makes answers worse.

[![CI](https://github.com/NickZivkovic1993/plumbline/actions/workflows/ci.yml/badge.svg)](https://github.com/NickZivkovic1993/plumbline/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

```text
$ plumbline run evals/support.yaml --baseline evals/baseline.json --fail-on regressions

contoso-support: 3 cases on qwen2.5:7b (live, http://localhost:11434/v1)

  PASS  refund-inside-window  (1/1, 16.3s)
  PASS  refund-after-90-days  (1/1, 14.3s)
  FAIL  refund-store-credit-window  (0/1, 19.0s)
      sample 0: fail matches /(?i)store\s+credit/ (answer does not match the pattern)
      sample 0: fail judge >= 4: Does not offer a cash refund, and offers store credit instead.
                (score 1/5: Does not address the refund policy or offer store credit, ...)
      answer: I understand your concern. Unfortunately, returns are not accepted after 30 days ...

2/3 passed on qwen2.5:7b in 73.3s
Baseline: 1 regressed, 0 fixed, 0 still failing, 0 new
  REGRESSED refund-store-credit-window: matches /(?i)store\s+credit/: answer does not match the pattern
```

That run is real: one line was deleted from the example bot's system prompt (the store-credit rule),
and the 7B model started telling customers that returns end at 30 days. Plumbline caught it and exited 1.

## Why

LLM features break quietly. Nothing throws when a reworded system prompt stops the bot from following
a policy, or when a cheaper model starts writing poems for people who asked a support bot for one.
Unit tests don't see it, and manual spot checks stop happening after week two.

Plumbline is the small, boring tool for that gap:

- **Suites in YAML**: prompts plus the checks their answers must pass, next to your code.
- **Cheap checks first**: `contains`, `notContains`, `regex`, `equals`, length limits, `jsonValid`, `jsonSchema`.
- **LLM-as-judge for the rest**: a plain-language criterion, scored 1-5 by a second model at temperature 0.
- **Sampling**: run a case N times and require a pass rate, because one lucky answer proves little.
- **Cassettes**: record answers once against a live model, commit them, replay them in CI.
  Deterministic, free, offline, no API keys in the pipeline.
- **Baselines**: compare against the last accepted run and fail only on *regressions*, so known
  weaknesses don't block every PR.
- **CI-native output**: JUnit XML, a Markdown job summary, JSON results, and a GitHub Action.
- **Any OpenAI-compatible endpoint**: Ollama, Groq, OpenAI, Azure OpenAI (v1 API), LM Studio, vLLM.
  From code, any `Microsoft.Extensions.AI` `IChatClient`.

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). For the live run, [Ollama](https://ollama.com)
with `qwen2.5:7b` and `qwen2.5:14b` pulled.

```bash
git clone https://github.com/NickZivkovic1993/plumbline && cd plumbline

# Replay the recorded example: no model needed
dotnet run --project src/Plumbline.Cli -- run examples/support-bot/suite.yaml \
  --replay examples/support-bot/cassette.json --baseline examples/support-bot/baseline.json

# Run it live against your local models
dotnet run --project src/Plumbline.Cli -- run examples/support-bot/suite.yaml

# Same suite, bigger model: does it fix the known failure?
dotnet run --project src/Plumbline.Cli -- run examples/support-bot/suite.yaml --model qwen2.5:14b \
  --baseline examples/support-bot/baseline.json
```

To use it as a global tool, pack it locally (it isn't published to NuGet yet):

```bash
dotnet pack src/Plumbline.Cli -c Release -o ./packages
dotnet tool install --global Plumbline.Cli --add-source ./packages
plumbline --help
```

## Writing a suite

```yaml
name: contoso-support

model:
  baseUrl: http://localhost:11434/v1    # any OpenAI-compatible endpoint
  name: qwen2.5:7b
  apiKeyEnv: GROQ_API_KEY               # optional: env var holding the key
  temperature: 0.2
  maxTokens: 400

judge:                                  # optional: needed by `judge` assertions
  name: qwen2.5:14b                     # baseUrl and apiKeyEnv default to the model's

system: |
  You are the customer support assistant for Contoso Outdoor ...

samples: 1                              # defaults for every case
passRate: 1.0

cases:
  - id: refund-store-credit-window
    prompt: My hiking boots arrived 45 days ago and they're too small. Can I return them for cash?
    assert:
      - regex: (?i)store\s+credit
      - judge: Does not offer a cash refund, and offers store credit instead.
        minScore: 4                     # 1-5, default 4

  - id: greeting-tone
    prompt: hi, quick question about my order
    samples: 3                          # ask three times...
    passRate: 0.66                      # ...two good answers are enough
    assert:
      - maxLength: 500
      - judge: Responds in a friendly, concise way and asks for the details needed to help.

  - id: triage-safety-escalation
    system: |                           # per-case system prompt override
      You triage incoming support messages. Reply with JSON only ...
    prompt: The gas canister adapter you sold me started leaking while the stove was lit.
    assert:
      - jsonSchema: schemas/triage.json # path relative to the suite, or inline JSON
      - regex: '"urgency"\s*:\s*"high"'
```

| Assertion | Passes when |
| --- | --- |
| `contains: text` | the answer contains `text` (case-insensitive unless `ignoreCase: false`) |
| `notContains: text` | the answer does not contain `text` |
| `equals: text` | the trimmed answer equals `text` (case-sensitive unless `ignoreCase: true`) |
| `regex: pattern` | the .NET regex matches (multiline) |
| `minLength` / `maxLength` | the answer length is within bounds |
| `jsonValid: true` | the answer is JSON (code fences and surrounding prose are tolerated) |
| `jsonSchema: file-or-inline` | the answer's JSON satisfies the JSON Schema (draft 2020-12) |
| `judge: criterion` | the judge model scores the answer at least `minScore` out of 5 |

`plumbline validate suite.yaml` checks a suite without calling a model, and reports every problem at
once: unknown keys (with line numbers), duplicate ids, two checks in one list item, a `judge` check
without a judge model, missing schema files.

Reasoning models' `<think>` blocks are stripped before any check runs.

## Cassettes and baselines

```bash
# 1. Record against the live model (local Ollama, or a key in your shell)
plumbline run evals/suite.yaml --record evals/cassette.json --out evals/baseline.json

# 2. Commit both files. In CI, replay and compare:
plumbline run evals/suite.yaml --replay evals/cassette.json \
  --baseline evals/baseline.json --fail-on regressions --junit junit.xml
```

A cassette entry is keyed by a SHA-256 of everything that can change the answer: model, system prompt,
prompt, temperature, max tokens and sample index. Edit a prompt and the replay fails loudly with
"No recording for '...'; re-record it with --record". A stale cassette can't silently pass.

Comparing against a baseline classifies every case:

| | Baseline passed | Baseline failed | Not in baseline |
| --- | --- | --- | --- |
| **Passes now** | passing | fixed | new |
| **Fails now** | **regressed** | still failing | **new failure** |

`--fail-on regressions` exits 1 only for the bold cells. `--fail-on any` (the default) exits 1 on any
failing case. Exit code 2 means invalid input.

The example suite keeps one known failure on purpose: `qwen2.5:7b` still writes the ocean poem even
after the system prompt explicitly forbids creative writing. It's recorded in the baseline, so CI stays
green until something *else* breaks. Running the same suite with `--model qwen2.5:14b` passes 9/9,
but took about 2.8x as long on my machine (223s vs 80s). That's the trade-off the suite exists to make visible.

## GitHub Action

```yaml
- uses: actions/checkout@v7
- uses: NickZivkovic1993/plumbline@main
  with:
    suite: evals/suite.yaml
    replay: evals/cassette.json
    baseline: evals/baseline.json
    fail-on: regressions
```

Inputs: `suite` (required), `replay`, `baseline`, `fail-on`, `out`, `junit`, `args` (extra CLI flags)
and `dotnet-version`. The Markdown summary lands in the job summary automatically. This repository runs
its own example through the action on every push. See [ci.yml](.github/workflows/ci.yml).

To run live in CI instead of replaying, drop `replay` and pass the key through the environment:

```yaml
  env:
    GROQ_API_KEY: ${{ secrets.GROQ_API_KEY }}
```

## From code

`Plumbline.Core` has no CLI dependencies. Evaluate any `Microsoft.Extensions.AI` client, for example
from an xUnit test:

```csharp
using Plumbline;
using Plumbline.Models;
using Plumbline.Running;

IChatClient client = /* Azure OpenAI, OpenAI, Ollama, your decorated pipeline... */;

var suite = SuiteLoader.LoadFile("evals/suite.yaml");
var run = await new SuiteRunner(new ChatClientModel(client)).RunAsync(suite);

Assert.True(run.FailedCount == 0, string.Join("\n", run.Cases.Where(c => !c.Passed).Select(c => $"{c.Id}: {c.FirstFailure}")));
```

`IChatModel` is a single method, so wrapping your own service (with its retrieval, tools and
guardrails) is a few lines. Evaluate the feature, not just the raw model.

## Project layout

```text
src/Plumbline.Core      suite loading, assertions, model clients, cassettes, runner, reports
src/Plumbline.Cli       the `plumbline` command (packs as a dotnet tool)
tests/Plumbline.Tests   xUnit tests, offline: stubbed HTTP and fake models
examples/support-bot    example suite, JSON schema, recorded cassette and baseline
action.yml              composite GitHub Action
```

## Status and limits

Version 0.1: small and deliberately boring. Known gaps, roughly in the order I'd tackle them:

- single-turn prompts only (no multi-turn conversations or tool-call assertions yet)
- no streaming; latency is measured end to end
- judge scores are only as good as the judge model. Calibrate a judge criterion against a few answers
  you have graded by hand before trusting it
- the GitHub Action builds Plumbline from source on each run (about 11s on a GitHub-hosted runner)

## License

MIT
