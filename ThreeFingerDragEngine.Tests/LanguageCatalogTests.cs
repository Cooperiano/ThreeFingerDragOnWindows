using System.Text.RegularExpressions;
using TouchpadControl.Localization;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public sealed class LanguageCatalogTests
{
    [Theory]
    [InlineData("system", "zh-CN", "zh-Hans")]
    [InlineData("system", "en-US", "en")]
    [InlineData("system", "fr-FR", "en")]
    [InlineData("en", "zh-CN", "en")]
    [InlineData("zh-Hans", "en-US", "zh-Hans")]
    [InlineData(null, null, "en")]
    [InlineData("unknown", "zh-CN", "zh-Hans")]
    public void Resolve_RespectsChoiceAndSystemFallback(string? choice, string? system, string expected) =>
        Assert.Equal(expected, LanguageCatalog.Resolve(choice, system));

    [Fact]
    public void EveryKey_HasBothTranslationsAndMatchingPlaceholders()
    {
        Assert.True(LanguageCatalog.Keys.Count() >= 120);
        foreach(var key in LanguageCatalog.Keys)
        {
            string english = LanguageCatalog.Get(key, "en");
            string chinese = LanguageCatalog.Get(key, "zh-Hans");
            Assert.NotEqual(key, english);
            Assert.NotEqual(key, chinese);
            Assert.False(string.IsNullOrWhiteSpace(english));
            Assert.False(string.IsNullOrWhiteSpace(chinese));
            Assert.Equal(Regex.Matches(english, @"\{\d+\}").Select(match => match.Value),
                Regex.Matches(chinese, @"\{\d+\}").Select(match => match.Value));
        }
    }

    [Fact]
    public void Get_UsesEnglishFallbackForMissingLanguageOrUnknownText()
    {
        Assert.Equal("Language", LanguageCatalog.Get("Language", "fr"));
        Assert.Equal("未知", LanguageCatalog.Get("unknown", "zh-Hans"));
        Assert.Equal("Untranslated device name", LanguageCatalog.Get("Untranslated device name", "zh-Hans"));
    }

    [Theory]
    [InlineData("{}", "system")]
    [InlineData("{\"Language\":\"en\",\"ThreeFingerDrag\":true}", "en")]
    [InlineData("{\"Language\":\"zh-Hans\"}", "zh-Hans")]
    [InlineData("{\"Language\":\"zh-CN\"}", "zh-Hans")]
    [InlineData("{\"Language\":null}", "system")]
    [InlineData("{\"Language\":42}", "system")]
    [InlineData("{\"Language\":\"unsupported\"}", "system")]
    [InlineData("broken json", "system")]
    [InlineData("null", "system")]
    public void Preference_ReadsOnlyValidLanguageAndSupportsOlderSettings(string json, string expected) =>
        Assert.Equal(expected, LanguagePreference.Read(json));

    [Fact]
    public void Format_PreservesDeviceIdentity()
    {
        Assert.Equal("启用三指指针移动（ELAN1203）",
            LanguageCatalog.Format("Enable three-finger pointer movement ({0})", "zh-Hans", "ELAN1203"));
    }

    [Theory]
    [InlineData("Application menu", "应用菜单")]
    [InlineData("Hide window", "隐藏窗口")]
    [InlineData("Quit Touchpad Control?", "退出 Touchpad Control？")]
    [InlineData("Cancel", "取消")]
    public void Menu_UsesLocalizedActionLabels(string english, string chinese)
    {
        Assert.Equal(english, LanguageCatalog.Get(english, "en"));
        Assert.Equal(chinese, LanguageCatalog.Get(english, "zh-Hans"));
    }

    [Theory]
    [InlineData("Check current settings", "检查当前设置")]
    [InlineData("Reapply saved choice", "重新应用保存的选择")]
    [InlineData("Apply changes", "应用更改")]
    public void PointerActions_DistinguishReadFromWrite(string english, string chinese)
    {
        Assert.Equal(english, LanguageCatalog.Get(english, "en"));
        Assert.Equal(chinese, LanguageCatalog.Get(english, "zh-Hans"));
    }

    [Fact]
    public void PointerValues_ShowActualAndTargetWithoutConflatingThem()
    {
        Assert.Equal("触控板当前速度：20/20 · 保存的目标：9/20", LanguageCatalog.Format(
            "Current touchpad speed: {0}/20 · Saved target: {1}/20", "zh-Hans", 20, 9));
        Assert.Contains("9/20", LanguageCatalog.Format(
            "The touchpad speed will be set to {0}/20. This also changes Windows pointer settings and may affect a mouse. Cancel keeps your current settings.", "en", 9));
    }
}
