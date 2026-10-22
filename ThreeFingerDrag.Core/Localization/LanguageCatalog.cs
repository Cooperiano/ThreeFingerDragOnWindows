using System.Globalization;
using System.Text.Json;

namespace TouchpadControl.Localization;

/// <summary>Shared, packaged translations for the WinUI settings and WinForms overview.</summary>
public static class LanguageCatalog
{
    public const string SystemChoice = "system";
    public static IReadOnlyList<string> Choices { get; } = Array.AsReadOnly(new[] { SystemChoice, "zh-Hans", "en" });
    private static readonly Dictionary<string, Dictionary<string, string>> Entries = Load();
    private static readonly Dictionary<string, string> EnglishKeys = Entries.ToDictionary(entry => entry.Value["en"], entry => entry.Key, StringComparer.Ordinal);
    public static IEnumerable<string> Keys => Entries.Keys;

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = typeof(LanguageCatalog).Assembly.GetManifestResourceStream("TouchpadControl.Localization.Strings.json")
            ?? throw new InvalidOperationException("Packaged translations are missing.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
            ?? throw new InvalidOperationException("Packaged translations are empty.");
    }

    public static string NormalizeChoice(string? choice) => choice switch
    {
        "en" or "en-US" or "en-GB" => "en",
        "zh-Hans" or "zh-CN" or "zh" => "zh-Hans",
        _ => SystemChoice
    };

    public static string Resolve(string? choice, string? systemLanguage)
    {
        var normalized = NormalizeChoice(choice);
        if(normalized != SystemChoice) return normalized;
        return systemLanguage?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ? "zh-Hans" : "en";
    }

    public static string Get(string keyOrEnglish, string language)
    {
        var key = EnglishKeys.GetValueOrDefault(keyOrEnglish, keyOrEnglish);
        if(!Entries.TryGetValue(key, out var translations)) return keyOrEnglish;
        return translations.GetValueOrDefault(language, translations["en"]);
    }

    public static string Format(string keyOrEnglish, string language, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(keyOrEnglish, language), arguments);
}
