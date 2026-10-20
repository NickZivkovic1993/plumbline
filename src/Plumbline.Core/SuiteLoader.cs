using Plumbline.Assertions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Plumbline;

/// <summary>Reads a suite from YAML and validates it before anything calls a model.</summary>
public static class SuiteLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static Suite LoadFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new SuiteException($"Suite file not found: {path}");
        }

        return Parse(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath)!);
    }

    /// <param name="yaml">Suite contents.</param>
    /// <param name="baseDirectory">Directory that relative paths (JSON schemas) resolve against.</param>
    public static Suite Parse(string yaml, string baseDirectory)
    {
        SuiteDto dto;
        try
        {
            dto = Yaml.Deserialize<SuiteDto>(yaml) ?? throw new SuiteException("Suite file is empty.");
        }
        catch (YamlException ex)
        {
            var inner = ex.InnerException?.Message is { } m ? $" ({m})" : string.Empty;
            throw new SuiteException($"Invalid suite YAML at line {ex.Start.Line}: {ex.Message}{inner}", ex);
        }

        var errors = new List<string>();
        var name = Required(dto.Name, "name", errors);
        var model = ToModel(dto.Model, "model", errors);
        var judge = dto.Judge is null ? null : ToModel(dto.Judge, "judge", errors, fallback: model);

        if (dto.Cases is not { Count: > 0 })
        {
            errors.Add("suite has no cases");
        }

        var cases = new List<EvalCase>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (caseDto, index) in (dto.Cases ?? []).Select((c, i) => (c, i)))
        {
            var where = caseDto.Id is { Length: > 0 } id ? $"case '{id}'" : $"case #{index + 1}";
            var caseId = Required(caseDto.Id, $"{where}: id", errors);
            if (caseId.Length > 0 && !seen.Add(caseId))
            {
                errors.Add($"{where}: duplicate id");
            }

            var samples = caseDto.Samples ?? dto.Samples ?? 1;
            var passRate = caseDto.PassRate ?? dto.PassRate ?? 1.0;
            if (samples < 1)
            {
                errors.Add($"{where}: samples must be at least 1");
            }

            if (passRate is <= 0 or > 1)
            {
                errors.Add($"{where}: passRate must be in (0, 1]");
            }

            var assertions = (caseDto.Assert ?? []).Select((a, i) => ToAssertion(a, $"{where}, assertion #{i + 1}", baseDirectory, errors))
                .OfType<IAssertion>()
                .ToList();
            if (caseDto.Assert is not { Count: > 0 })
            {
                errors.Add($"{where}: has no assertions");
            }

            if (assertions.OfType<JudgeAssertion>().Any() && judge is null)
            {
                errors.Add($"{where}: uses 'judge' but the suite has no 'judge:' model");
            }

            cases.Add(new EvalCase(caseId, Required(caseDto.Prompt, $"{where}: prompt", errors), assertions, samples, passRate, caseDto.System));
        }

        if (errors.Count > 0)
        {
            throw new SuiteException("Suite is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
        }

        return new Suite(name, dto.Description, model!, judge, dto.System, cases);
    }

    private static ModelSettings? ToModel(ModelDto? dto, string section, List<string> errors, ModelSettings? fallback = null)
    {
        if (dto is null)
        {
            errors.Add($"'{section}:' section is required");
            return null;
        }

        var name = dto.Name ?? (section == "judge" ? fallback?.Name : null);
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add($"{section}.name is required");
        }

        var baseUrlText = dto.BaseUrl ?? fallback?.BaseUrl.ToString();
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl))
        {
            errors.Add($"{section}.baseUrl must be an absolute URL (e.g. http://localhost:11434/v1)");
        }

        return new ModelSettings(
            name ?? string.Empty,
            baseUrl ?? new Uri("http://invalid"),
            dto.ApiKeyEnv ?? fallback?.ApiKeyEnv,
            dto.Temperature,
            dto.MaxTokens);
    }

    private static IAssertion? ToAssertion(AssertionDto dto, string where, string baseDirectory, List<string> errors)
    {
        var kinds = new (string Kind, bool Present)[]
        {
            ("contains", dto.Contains is not null),
            ("notContains", dto.NotContains is not null),
            ("equals", dto.EqualTo is not null),
            ("regex", dto.Regex is not null),
            ("length", dto.MinLength is not null || dto.MaxLength is not null),
            ("jsonValid", dto.JsonValid == true),
            ("jsonSchema", dto.JsonSchema is not null),
            ("judge", dto.Judge is not null),
        }.Where(k => k.Present).Select(k => k.Kind).ToList();

        if (kinds.Count != 1)
        {
            errors.Add(kinds.Count == 0
                ? $"{where}: unknown or empty assertion"
                : $"{where}: one assertion per list item, found {string.Join(" + ", kinds)}");
            return null;
        }

        try
        {
            return kinds[0] switch
            {
                "contains" => new ContainsAssertion(dto.Contains!, dto.IgnoreCase ?? true),
                "notContains" => new NotContainsAssertion(dto.NotContains!, dto.IgnoreCase ?? true),
                "equals" => new EqualsAssertion(dto.EqualTo!, dto.IgnoreCase ?? false),
                "regex" => new RegexAssertion(dto.Regex!, dto.IgnoreCase ?? false),
                "length" => new LengthAssertion(dto.MinLength, dto.MaxLength),
                "jsonValid" => new JsonValidAssertion(),
                "jsonSchema" => LoadSchema(dto.JsonSchema!, baseDirectory),
                "judge" => (dto.MinScore ?? 4) is >= 1 and <= 5
                    ? new JudgeAssertion(dto.Judge!, dto.MinScore ?? 4)
                    : throw new ArgumentException("minScore must be between 1 and 5"),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Text.Json.JsonException)
        {
            errors.Add($"{where}: {ex.Message}");
            return null;
        }
    }

    private static JsonSchemaAssertion LoadSchema(string schema, string baseDirectory)
    {
        // Inline schemas start with '{'; anything else is a path relative to the suite file.
        if (schema.TrimStart().StartsWith('{'))
        {
            return new JsonSchemaAssertion(schema, "(inline)");
        }

        var path = Path.GetFullPath(Path.Combine(baseDirectory, schema));
        if (!File.Exists(path))
        {
            throw new IOException($"schema file not found: {schema}");
        }

        return new JsonSchemaAssertion(File.ReadAllText(path), schema);
    }

    private static string Required(string? value, string what, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{what} is required");
            return string.Empty;
        }

        return value;
    }

    private sealed class SuiteDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public ModelDto? Model { get; set; }
        public ModelDto? Judge { get; set; }
        public string? System { get; set; }
        public int? Samples { get; set; }
        public double? PassRate { get; set; }
        public List<CaseDto>? Cases { get; set; }
    }

    private sealed class ModelDto
    {
        public string? Name { get; set; }
        public string? BaseUrl { get; set; }
        public string? ApiKeyEnv { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
    }

    private sealed class CaseDto
    {
        public string? Id { get; set; }
        public string? Prompt { get; set; }
        public string? System { get; set; }
        public int? Samples { get; set; }
        public double? PassRate { get; set; }
        public List<AssertionDto>? Assert { get; set; }
    }

    private sealed class AssertionDto
    {
        public string? Contains { get; set; }
        public string? NotContains { get; set; }

        [YamlMember(Alias = "equals")]
        public string? EqualTo { get; set; }

        public string? Regex { get; set; }
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public bool? JsonValid { get; set; }
        public string? JsonSchema { get; set; }
        public string? Judge { get; set; }
        public int? MinScore { get; set; }
        public bool? IgnoreCase { get; set; }
    }
}

public sealed class SuiteException(string message, Exception? inner = null) : Exception(message, inner);
