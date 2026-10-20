using Plumbline.Assertions;

namespace Plumbline.Tests;

public class SuiteLoaderTests
{
    private const string Minimal =
        """
        name: demo
        model:
          baseUrl: http://localhost:11434/v1
          name: qwen2.5:7b
        cases:
          - id: one
            prompt: hello
            assert:
              - contains: hi
        """;

    private static Suite Parse(string yaml) => SuiteLoader.Parse(yaml, Repo.Path("examples", "support-bot"));

    private static string Errors(string yaml) => Assert.Throws<SuiteException>(() => Parse(yaml)).Message;

    [Fact]
    public void Loads_the_example_suite()
    {
        var suite = SuiteLoader.LoadFile(Repo.Path("examples", "support-bot", "suite.yaml"));

        Assert.Equal("contoso-support", suite.Name);
        Assert.Equal("qwen2.5:7b", suite.Model.Name);
        Assert.Equal("qwen2.5:14b", suite.Judge?.Name);
        Assert.Equal(9, suite.Cases.Count);
        Assert.Contains("Contoso Outdoor", suite.System);

        var greeting = suite.Cases.Single(c => c.Id == "greeting-tone");
        Assert.Equal(3, greeting.Samples);
        Assert.Equal(0.66, greeting.PassRate);

        var triage = suite.Cases.Single(c => c.Id == "triage-json-shape");
        Assert.NotNull(triage.System);
        Assert.IsType<JsonSchemaAssertion>(triage.Assertions[0]);
    }

    [Fact]
    public void Maps_every_assertion_kind()
    {
        var suite = Parse(Minimal.Replace(
            "      - contains: hi",
            """
                  - contains: hi
                  - notContains: bye
                  - equals: hello
                  - regex: h.llo
                  - maxLength: 10
                  - jsonValid: true
                  - jsonSchema: '{"type": "object"}'
                  - judge: Is friendly.
                    minScore: 3
            """.TrimEnd()) + "\njudge:\n  name: big-model\n");

        Assert.Collection(suite.Cases[0].Assertions,
            a => Assert.IsType<ContainsAssertion>(a),
            a => Assert.IsType<NotContainsAssertion>(a),
            a => Assert.IsType<EqualsAssertion>(a),
            a => Assert.IsType<RegexAssertion>(a),
            a => Assert.IsType<LengthAssertion>(a),
            a => Assert.IsType<JsonValidAssertion>(a),
            a => Assert.IsType<JsonSchemaAssertion>(a),
            a => Assert.Equal(3, Assert.IsType<JudgeAssertion>(a).MinScore));
    }

    [Fact]
    public void Judge_inherits_endpoint_from_the_model()
    {
        var suite = Parse(Minimal + "\njudge:\n  name: qwen2.5:14b\n");
        Assert.Equal(suite.Model.BaseUrl, suite.Judge!.BaseUrl);
        Assert.Equal("qwen2.5:14b", suite.Judge.Name);
    }

    [Fact]
    public void Suite_level_samples_and_pass_rate_are_defaults()
    {
        var suite = Parse(Minimal + "\nsamples: 5\npassRate: 0.8\n");
        Assert.Equal(5, suite.Cases[0].Samples);
        Assert.Equal(0.8, suite.Cases[0].PassRate);
    }

    [Fact]
    public void Reports_every_problem_at_once()
    {
        var message = Errors(
            """
            name: broken
            model:
              baseUrl: not-a-url
            cases:
              - id: a
                prompt: x
                assert:
                  - contains: y
                    regex: z
              - id: a
                assert:
                  - judge: needs a judge
            """);

        Assert.Contains("model.name is required", message);
        Assert.Contains("baseUrl must be an absolute URL", message);
        Assert.Contains("one assertion per list item, found contains + regex", message);
        Assert.Contains("duplicate id", message);
        Assert.Contains("prompt is required", message);
        Assert.Contains("no 'judge:' model", message);
    }

    [Theory]
    [InlineData("samples: 0", "samples must be at least 1")]
    [InlineData("passRate: 1.5", "passRate must be in (0, 1]")]
    public void Validates_sampling_settings(string line, string expected) =>
        Assert.Contains(expected, Errors(Minimal + "\n" + line + "\n"));

    [Fact]
    public void Rejects_unknown_keys_with_a_line_number()
    {
        var message = Errors(Minimal.Replace("prompt: hello", "prompt: hello\n    promt: typo"));
        Assert.Contains("line", message);
    }

    [Fact]
    public void Rejects_a_suite_without_cases() =>
        Assert.Contains("suite has no cases", Errors("name: x\nmodel:\n  name: m\n  baseUrl: http://localhost/v1\n"));

    [Fact]
    public void Reports_a_missing_schema_file() =>
        Assert.Contains("schema file not found", Errors(Minimal.Replace("contains: hi", "jsonSchema: schemas/nope.json")));

    [Fact]
    public void Reports_a_missing_suite_file() =>
        Assert.Contains("not found", Assert.Throws<SuiteException>(() => SuiteLoader.LoadFile("does-not-exist.yaml")).Message);
}
