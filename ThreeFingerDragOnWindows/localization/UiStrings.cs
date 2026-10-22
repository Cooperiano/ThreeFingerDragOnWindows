#nullable enable
using System;
using System.Globalization;
using Microsoft.UI.Xaml.Markup;
using TouchpadControl.Localization;

namespace ThreeFingerDragOnWindows.localization;

public static class UiStrings
{
    private static string _choice = LanguageCatalog.SystemChoice;
    private static readonly string SystemLanguage = GetSystemLanguage();
    public static string Choice => _choice;
    public static string ResolvedLanguage => LanguageCatalog.Resolve(_choice, SystemLanguage);
    public static void SetLanguage(string? choice) => _choice = LanguageCatalog.NormalizeChoice(choice);
    public static string Get(string keyOrEnglish) => LanguageCatalog.Get(keyOrEnglish, ResolvedLanguage);
    public static string Format(string keyOrEnglish, params object[] arguments) => LanguageCatalog.Format(keyOrEnglish, ResolvedLanguage, arguments);

    private static string GetSystemLanguage()
    {
        try { return Windows.System.UserProfile.GlobalizationPreferences.Languages[0]; }
        catch { return CultureInfo.CurrentUICulture.Name; }
    }
}

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class Translate : MarkupExtension
{
    public string Key { get; set; } = "";
    protected override object ProvideValue() => UiStrings.Get(Key);
}
