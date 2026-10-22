using System.Text.Json;

namespace TouchpadControl.Localization;

public static class LanguagePreference
{
    // Only read the language field. Never rewrite the user's preferences from the companion.
    public static string Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Language", out var value)
                && value.ValueKind == JsonValueKind.String
                ? LanguageCatalog.NormalizeChoice(value.GetString()) : LanguageCatalog.SystemChoice;
        }
        catch(JsonException) { return LanguageCatalog.SystemChoice; }
    }
}
