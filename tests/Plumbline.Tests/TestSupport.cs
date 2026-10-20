using System.Net;
using Plumbline.Models;

namespace Plumbline.Tests;

/// <summary>Answers by prompt; records every request it saw.</summary>
internal sealed class FakeModel(Func<ModelRequest, string> answer) : IChatModel
{
    private readonly List<ModelRequest> _requests = [];

    public FakeModel(string fixedAnswer) : this(_ => fixedAnswer)
    {
    }

    public IReadOnlyList<ModelRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(new ModelReply(answer(request), 10, 5));
    }
}

internal sealed class FailingModel(string message) : IChatModel
{
    public Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default) =>
        throw new ModelException(message);
}

/// <summary>Scripted HTTP responses for testing the OpenAI-compatible client without a network.</summary>
internal sealed class StubHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
{
    private int _next;

    public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Seen.Add((request, body));
        var (status, responseBody) = responses[Math.Min(_next++, responses.Length - 1)];
        return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
    }
}

internal static class Repo
{
    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    public static string Root { get; } = FindRoot();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Plumbline.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}

internal sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "plumbline-tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp cleaner gets the rest.
        }
    }
}
