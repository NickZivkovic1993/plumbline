using Plumbline.Assertions;

namespace Plumbline;

/// <summary>A named set of eval cases run against one model.</summary>
public sealed record Suite(
    string Name,
    string? Description,
    ModelSettings Model,
    ModelSettings? Judge,
    string? System,
    IReadOnlyList<EvalCase> Cases);

/// <summary>Where and how to reach a chat model through an OpenAI-compatible endpoint.</summary>
public sealed record ModelSettings(
    string Name,
    Uri BaseUrl,
    string? ApiKeyEnv = null,
    double? Temperature = null,
    int? MaxTokens = null)
{
    /// <summary>Reads the API key from the configured environment variable, if any.</summary>
    public string? ResolveApiKey() =>
        string.IsNullOrWhiteSpace(ApiKeyEnv) ? null : Environment.GetEnvironmentVariable(ApiKeyEnv);
}

/// <summary>
/// One prompt and the checks its answer must pass. A case is sampled <see cref="Samples"/> times
/// and passes when at least <see cref="PassRate"/> of the samples pass every assertion.
/// </summary>
public sealed record EvalCase(
    string Id,
    string Prompt,
    IReadOnlyList<IAssertion> Assertions,
    int Samples = 1,
    double PassRate = 1.0,
    string? System = null);
