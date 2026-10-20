using Plumbline.Models;

namespace Plumbline.Tests;

public class CassetteTests
{
    private static readonly ModelRequest Request = new("m", "sys", "prompt", 0.2, 100, 0, "case-a");

    [Fact]
    public void Key_ignores_the_label_but_not_the_content()
    {
        var key = Cassette.KeyOf(Request);
        Assert.Equal(key, Cassette.KeyOf(Request with { Label = "renamed" }));
        Assert.NotEqual(key, Cassette.KeyOf(Request with { Prompt = "prompt!" }));
        Assert.NotEqual(key, Cassette.KeyOf(Request with { System = "other" }));
        Assert.NotEqual(key, Cassette.KeyOf(Request with { Model = "m2" }));
        Assert.NotEqual(key, Cassette.KeyOf(Request with { Sample = 1 }));
        Assert.NotEqual(key, Cassette.KeyOf(Request with { Temperature = 0.3 }));
    }

    [Fact]
    public async Task Records_saves_loads_and_replays()
    {
        using var temp = new TempDir();
        var path = temp.File("nested/cassette.json");
        var cassette = new Cassette();
        var live = new FakeModel("Café's answer ✓");

        await new RecordingModel(live, cassette).CompleteAsync(Request);
        cassette.Save(path);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("Café's answer ✓", text);
        Assert.Contains("\"label\": \"case-a\"", text);

        var replayed = await new ReplayModel(Cassette.Load(path)).CompleteAsync(Request with { Label = "anything" });
        Assert.Equal("Café's answer ✓", replayed.Text);
        Assert.Equal(10, replayed.InputTokens);
        Assert.Single(live.Requests);
    }

    [Fact]
    public async Task Replay_fails_loudly_on_unrecorded_requests()
    {
        var ex = await Assert.ThrowsAsync<ModelException>(() =>
            new ReplayModel(new Cassette()).CompleteAsync(Request));
        Assert.Contains("re-record", ex.Message);
        Assert.Contains("case-a", ex.Message);
    }

    [Fact]
    public void Rejects_other_format_versions()
    {
        using var temp = new TempDir();
        var path = temp.File("old.json");
        File.WriteAllText(path, """{"version": 99, "entries": []}""");
        Assert.Throws<InvalidDataException>(() => Cassette.Load(path));
    }
}
