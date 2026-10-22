using System.Globalization;
using TouchpadControl.Localization;

namespace MissionControlPrototype;

internal static class OverviewLanguage
{
    private static string _language = LanguageCatalog.Resolve("system", CultureInfo.CurrentUICulture.Name);
    public static string Get(string keyOrEnglish) => LanguageCatalog.Get(keyOrEnglish, _language);

    public static void Refresh()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", "50931ClmentGrennerat.ThreeFingersDragOnWindows_cvkce2k9t2r60", "LocalState", "preferences.json");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            _language = LanguageCatalog.Resolve(LanguagePreference.Read(reader.ReadToEnd()), CultureInfo.CurrentUICulture.Name);
        }
        catch(IOException) { /* Keep the last working language if a save is in progress. */ }
        catch(UnauthorizedAccessException) { }
    }
}
