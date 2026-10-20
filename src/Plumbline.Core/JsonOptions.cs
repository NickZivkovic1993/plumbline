using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plumbline;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly JsonSerializerOptions Indented = new(Default)
    {
        WriteIndented = true,
        // Keep model output readable in committed files (apostrophes, accents, emoji).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
