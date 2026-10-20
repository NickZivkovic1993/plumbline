using System.Text.RegularExpressions;

namespace Plumbline.Models;

/// <summary>Normalisation applied to raw model output before any assertion sees it.</summary>
public static partial class ModelText
{
    /// <summary>Removes &lt;think&gt; blocks emitted by reasoning models (Qwen3, DeepSeek-R1, ...).</summary>
    public static string StripThinking(string text) => ThinkBlock().Replace(text, string.Empty).Trim();

    /// <summary>
    /// Returns the JSON payload of an answer: the body of a ```json fence when present,
    /// otherwise the text from the first '{' or '[' to the matching last '}' or ']'.
    /// </summary>
    public static string ExtractJson(string text)
    {
        var fence = JsonFence().Match(text);
        if (fence.Success)
        {
            return fence.Groups["body"].Value.Trim();
        }

        var start = text.IndexOfAny(['{', '[']);
        if (start < 0)
        {
            return text.Trim();
        }

        var close = text[start] == '{' ? '}' : ']';
        var end = text.LastIndexOf(close);
        return end > start ? text[start..(end + 1)] : text[start..].Trim();
    }

    [GeneratedRegex(@"<think>.*?(</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"```(?:json)?\s*\n(?<body>.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonFence();
}
