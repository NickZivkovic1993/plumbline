using Plumbline.Models;

namespace Plumbline.Tests;

public class ModelTextTests
{
    [Theory]
    [InlineData("<think>plan</think>Answer", "Answer")]
    [InlineData("<THINK>\nmulti\nline\n</THINK>\n\nAnswer", "Answer")]
    [InlineData("<think>never closed", "")]
    [InlineData("No reasoning here", "No reasoning here")]
    public void StripThinking_removes_reasoning_blocks(string input, string expected) =>
        Assert.Equal(expected, ModelText.StripThinking(input));

    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("```\n[1,2]\n```", "[1,2]")]
    [InlineData("Result: {\"a\": {\"b\": 2}} done", "{\"a\": {\"b\": 2}}")]
    [InlineData("[1, 2, 3] and more", "[1, 2, 3]")]
    [InlineData("plain text", "plain text")]
    public void ExtractJson_finds_the_payload(string input, string expected) =>
        Assert.Equal(expected, ModelText.ExtractJson(input));
}
