using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plumbline.Models;

/// <summary>
/// Recorded model answers keyed by request. Record once against a live model, commit the file,
/// and CI replays it: deterministic, free, and no API keys in the pipeline.
/// </summary>
public sealed class Cassette
{
    private const int FormatVersion = 1;

    private readonly ConcurrentDictionary<string, CassetteEntry> _entries;

    public Cassette() => _entries = new ConcurrentDictionary<string, CassetteEntry>();

    private Cassette(IEnumerable<CassetteEntry> entries) =>
        _entries = new ConcurrentDictionary<string, CassetteEntry>(entries.Select(e => KeyValuePair.Create(e.Key, e)));

    public int Count => _entries.Count;

    public static Cassette Load(string path)
    {
        var file = JsonSerializer.Deserialize<CassetteFile>(File.ReadAllText(path), JsonOptions.Default)
            ?? throw new InvalidDataException($"{path} is not a Plumbline cassette.");
        if (file.Version != FormatVersion)
        {
            throw new InvalidDataException($"{path} is cassette format v{file.Version}; this build reads v{FormatVersion}.");
        }

        return new Cassette(file.Entries);
    }

    public void Save(string path)
    {
        // Sorted by label so re-recording produces readable diffs.
        var file = new CassetteFile(FormatVersion, [.. _entries.Values.OrderBy(e => e.Label, StringComparer.Ordinal).ThenBy(e => e.Key)]);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions.Indented) + Environment.NewLine);
    }

    public bool TryGet(ModelRequest request, out ModelReply reply)
    {
        if (_entries.TryGetValue(KeyOf(request), out var entry))
        {
            reply = new ModelReply(entry.Text, entry.InputTokens, entry.OutputTokens);
            return true;
        }

        reply = null!;
        return false;
    }

    public void Put(ModelRequest request, ModelReply reply)
    {
        var key = KeyOf(request);
        _entries[key] = new CassetteEntry(key, request.Label ?? key[..12], request.Model, reply.Text, reply.InputTokens, reply.OutputTokens);
    }

    /// <summary>Stable hash of everything that can change a model's answer. The label is deliberately excluded.</summary>
    public static string KeyOf(ModelRequest request)
    {
        var material = JsonSerializer.Serialize(new
        {
            request.Model,
            request.System,
            request.Prompt,
            request.Temperature,
            request.MaxTokens,
            request.Sample,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private sealed record CassetteFile(int Version, List<CassetteEntry> Entries);

    private sealed record CassetteEntry(
        string Key,
        string Label,
        string Model,
        string Text,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? InputTokens,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? OutputTokens);
}

/// <summary>Serves answers from a cassette and fails loudly on anything it has not seen.</summary>
public sealed class ReplayModel(Cassette cassette) : IChatModel
{
    public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
        cassette.TryGet(request, out var reply)
            ? Task.FromResult(reply)
            : throw new ModelException(
                $"No recording for '{request.Label ?? request.Prompt}'. The prompt, system prompt or model changed since the cassette was recorded; re-record it with --record.");
}

/// <summary>Passes requests to a live model and writes every answer into the cassette.</summary>
public sealed class RecordingModel(IChatModel inner, Cassette cassette) : IChatModel
{
    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        var reply = await inner.CompleteAsync(request, cancellationToken);
        cassette.Put(request, reply);
        return reply;
    }
}
