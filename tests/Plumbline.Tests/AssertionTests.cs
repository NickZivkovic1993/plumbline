using Plumbline.Assertions;

namespace Plumbline.Tests;

public class AssertionTests
{
    private static Task<AssertionResult> Run(IAssertion assertion, string output) =>
        assertion.EvaluateAsync(new AssertionContext("prompt", output)).AsTask();

    [Theory]
    [InlineData("Full REFUND available", true)]
    [InlineData("store credit only", false)]
    public async Task Contains_is_case_insensitive_by_default(string output, bool expected)
    {
        var result = await Run(new ContainsAssertion("refund"), output);
        Assert.Equal(expected, result.Passed);
    }

    [Fact]
    public async Task Contains_can_be_case_sensitive()
    {
        var result = await Run(new ContainsAssertion("Refund", ignoreCase: false), "refund");
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task NotContains_fails_when_forbidden_text_appears()
    {
        var result = await Run(new NotContainsAssertion("100% off"), "Sure, use code X for 100% OFF");
        Assert.False(result.Passed);
        Assert.Contains("forbidden", result.Message);
    }

    [Fact]
    public async Task Equals_ignores_surrounding_whitespace()
    {
        Assert.True((await Run(new EqualsAssertion("shipping"), "  shipping\n")).Passed);
        Assert.False((await Run(new EqualsAssertion("shipping"), "Shipping")).Passed);
    }

    [Fact]
    public async Task Regex_matches_multiline_output()
    {
        var result = await Run(new RegexAssertion(@"^\s*""urgency""\s*:\s*""high"""), "{\n  \"urgency\": \"high\"\n}");
        Assert.True(result.Passed);
    }

    [Theory]
    [InlineData(5, 10, "abc", false)]
    [InlineData(null, 3, "abcd", false)]
    [InlineData(2, null, "abc", true)]
    public async Task Length_checks_bounds(int? min, int? max, string output, bool expected)
    {
        Assert.Equal(expected, (await Run(new LengthAssertion(min, max), output)).Passed);
    }

    [Theory]
    [InlineData("{\"a\": 1}", true)]
    [InlineData("Here you go:\n```json\n{\"a\": 1}\n```", true)]
    [InlineData("Sure! {\"a\": 1} Hope that helps.", true)]
    [InlineData("{\"a\": 1,}", false)]
    [InlineData("no json here", false)]
    public async Task JsonValid_tolerates_fences_and_prose(string output, bool expected)
    {
        Assert.Equal(expected, (await Run(new JsonValidAssertion(), output)).Passed);
    }

    private const string TriageSchema =
        """
        {
          "type": "object",
          "required": ["category", "urgency"],
          "additionalProperties": false,
          "properties": {
            "category": { "enum": ["refund", "shipping"] },
            "urgency": { "enum": ["low", "high"] }
          }
        }
        """;

    [Fact]
    public async Task JsonSchema_passes_conforming_output()
    {
        var result = await Run(new JsonSchemaAssertion(TriageSchema, "triage"), "{\"category\":\"refund\",\"urgency\":\"low\"}");
        Assert.True(result.Passed, result.Message);
    }

    [Theory]
    [InlineData("{\"category\":\"billing\",\"urgency\":\"low\"}")]
    [InlineData("{\"category\":\"refund\"}")]
    [InlineData("{\"category\":\"refund\",\"urgency\":\"low\",\"extra\":1}")]
    public async Task JsonSchema_fails_with_a_reason(string output)
    {
        var result = await Run(new JsonSchemaAssertion(TriageSchema, "triage"), output);
        Assert.False(result.Passed);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task JsonSchema_reports_unparseable_json()
    {
        var result = await Run(new JsonSchemaAssertion(TriageSchema, "triage"), "category: refund");
        Assert.False(result.Passed);
        Assert.StartsWith("not valid JSON", result.Message);
    }
}
