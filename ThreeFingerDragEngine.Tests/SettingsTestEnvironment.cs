// These shims compile the actual settings sources into an isolated, non-UI test host.
// Nothing in this host resolves the real package LocalState or injects native input.
namespace Windows.Storage
{
    public sealed class ApplicationData
    {
        public static ApplicationData Current { get; } = new();
        public TestLocalFolder LocalFolder { get; } = new();
    }
    public sealed class TestLocalFolder
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "TouchpadControlSettingsTestHost-" + System.Guid.NewGuid().ToString("N"));
    }
}

namespace ThreeFingerDragOnWindows.utils
{
    public static class Logger { public static void Log(string message) { } }
    public static class RuntimeDiagnostics { public static void Write(string category, string message) { } }
}

namespace ThreeFingerDragOnWindows.localization
{
    public static class UiStrings
    {
        public static string Format(string format, object argument) => string.Format(format, argument);
    }
}

namespace ThreeFingerDragOnWindows
{
    public static class App
    {
        public static settings.SettingsData SettingsData { get; set; } = new();
    }
}

namespace ThreeFingerDragOnWindows.touchpad
{
    public sealed class TouchpadDeviceInfo
    {
        public string? deviceId = "settings-test-device";
        public override string ToString() => "Isolated test device";
    }
}
