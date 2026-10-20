using System.Text.Json;
using Json.Schema;
using Plumbline.Models;

namespace Plumbline.Assertions;

/// <summary>The answer must be parseable JSON. Code fences and surrounding prose are tolerated.</summary>
public sealed class JsonValidAssertion : IAssertion
{
    public string Name => "is valid JSON";

    public ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var _ = JsonDocument.Parse(ModelText.ExtractJson(context.Output));
            return ValueTask.FromResult(AssertionResult.Pass(Name));
        }
        catch (JsonException ex)
        {
            return ValueTask.FromResult(AssertionResult.Fail(Name, $"not valid JSON: {ex.Message}"));
        }
    }
}

/// <summary>The answer must be JSON that satisfies a JSON Schema (draft 2020-12 by default).</summary>
public sealed class JsonSchemaAssertion : IAssertion
{
    private readonly JsonSchema _schema;

    public JsonSchemaAssertion(string schemaJson, string displayName)
    {
        _schema = JsonSchema.FromText(schemaJson);
        Name = $"matches schema {displayName}";
    }

    public string Name { get; }

    public ValueTask<AssertionResult> EvaluateAsync(AssertionContext context, CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(ModelText.ExtractJson(context.Output));
        }
        catch (JsonException ex)
        {
            return ValueTask.FromResult(AssertionResult.Fail(Name, $"not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            var results = _schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (results.IsValid)
            {
                return ValueTask.FromResult(AssertionResult.Pass(Name));
            }

            var errors = Flatten(results).Take(3).ToList();
            var detail = errors.Count == 0 ? "schema validation failed" : string.Join("; ", errors);
            return ValueTask.FromResult(AssertionResult.Fail(Name, detail));
        }
    }

    private static IEnumerable<string> Flatten(EvaluationResults results)
    {
        if (results.Errors is { } errors)
        {
            foreach (var (keyword, message) in errors)
            {
                var location = results.InstanceLocation.ToString();
                yield return $"{(string.IsNullOrEmpty(location) ? "/" : location)} {keyword}: {message}";
            }
        }

        foreach (var child in results.Details ?? [])
        {
            foreach (var error in Flatten(child))
            {
                yield return error;
            }
        }
    }
}
