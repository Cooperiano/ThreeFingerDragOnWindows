using System.Text.Json;
using Xunit;
using ThreeFingerDragOnWindows;
using ThreeFingerDragOnWindows.settings;
using ThreeFingerDragOnWindows.touchpad;
using TouchpadControl.Settings;

namespace ThreeFingerDragEngine.Tests;

public sealed class SettingsPersistenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TouchpadControl-settings-" + Guid.NewGuid().ToString("N"));
    private string PreferencesPath => Path.Combine(_folder, "preferences.json");

    public SettingsPersistenceTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void OlderVersions_ExecuteEveryApplicableMigration(int version)
    {
        File.WriteAllText(PreferencesPath, $$"""{"SettingsVersion":{{version}},"FourFingerSwipeThreshold":180,"ThreeFingerDragStartThreshold":100}""");
        var notifications = 0;
        void Changed() => notifications++;
        SettingsData.PersistenceChanged += Changed;
        try
        {
            var settings = SettingsData.LoadFromPath(PreferencesPath);
            Assert.Equal(9, settings.SettingsVersion);
            Assert.Equal(90, settings.FourFingerSwipeThreshold);
            Assert.Equal(32, settings.ThreeFingerDragStartThreshold);
            Assert.Equal(1, notifications);
            var loaded = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(PreferencesPath))!;
            Assert.Equal(90, loaded.FourFingerSwipeThreshold);
            Assert.Equal(32, loaded.ThreeFingerDragStartThreshold);
        }
        finally { SettingsData.PersistenceChanged -= Changed; }
    }

    [Fact]
    public void Migration_PreservesCustomThresholds()
    {
        var migrated = SettingsMigration.Apply(7, 230, 75);
        Assert.Equal(230, migrated.FourFingerThreshold);
        Assert.Equal(75, migrated.StartDragThreshold);
        Assert.True(migrated.RequiresSave);
    }

    [Fact]
    public void Version8_MigratesStartOnly()
    {
        var migrated = SettingsMigration.Apply(8, 180, 100);
        Assert.Equal(180, migrated.FourFingerThreshold);
        Assert.Equal(32, migrated.StartDragThreshold);
    }

    [Fact]
    public void CurrentVersion_LoadAndUnchangedSavePreserveExactBytesAndStoredIntent()
    {
        var original = " { \"SettingsVersion\" : 9, \"MacLikePointerTuning\":true, \"Language\":\"zh-Hans\", \"ThreeFingerDragStartThreshold\":32 }\n";
        File.WriteAllText(PreferencesPath, original);
        var before = File.ReadAllBytes(PreferencesPath);
        var settings = SettingsData.LoadFromPath(PreferencesPath);
        Assert.True(settings.MacLikePointerTuning);
        Assert.Equal("zh-Hans", settings.Language);
        settings.save();
        Assert.Equal(before, File.ReadAllBytes(PreferencesPath));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void NewPreferences_DoNotClaimAnAppliedPointerProfile()
    {
        var settings = SettingsData.LoadFromPath(PreferencesPath);
        Assert.False(settings.MacLikePointerTuning);
        Assert.False(settings.PointerBackupCaptured);
        Assert.Equal(9, settings.SettingsVersion);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"SettingsVersion\":10}")]
    [InlineData("{\"SettingsVersion\":9,\"FourFingerSwipeThreshold\":0}")]
    public void InvalidDocument_IsPreservedAndCannotBeOverwrittenByFallback(string original)
    {
        File.WriteAllText(PreferencesPath, original);
        var settings = SettingsData.LoadFromPath(PreferencesPath);
        Assert.True(settings.IsPersistenceProtected);
        Assert.False(settings.ThreeFingerDrag);
        Assert.False(settings.FourFingerGestures);
        Assert.False(settings.TryCommit(candidate => candidate.Language = "en").Success);
        Assert.Throws<InvalidOperationException>(() => settings.save());
        Assert.Equal(original, File.ReadAllText(PreferencesPath));
    }

    [Fact]
    public void FailedWrite_PreservesAllLiveValuesAndDeviceConfiguration()
    {
        var settings = SettingsData.LoadFromPath(PreferencesPath);
        settings.TryCommit(candidate => candidate.ThreeFingerDeviceDragCursorConfigs["device"] = new(true, 30, 10));
        var heldConfig = settings.ThreeFingerDeviceDragCursorConfigs["device"];
        var original = File.ReadAllText(PreferencesPath);
        using(var blocked = new FileStream(PreferencesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = settings.TryCommit(candidate =>
            {
                candidate.ThreeFingerDrag = false;
                candidate.ThreeFingerDeviceDragCursorConfigs["device"].ThreeFingerDragCursorSpeed = 80;
            });
            Assert.False(result.Success);
            Assert.True(settings.ThreeFingerDrag);
            Assert.Equal(30, settings.ThreeFingerDeviceDragCursorConfigs["device"].ThreeFingerDragCursorSpeed);
            Assert.Same(heldConfig, settings.ThreeFingerDeviceDragCursorConfigs["device"]);
        }
        Assert.Equal(original, File.ReadAllText(PreferencesPath));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void Commit_PublishesAfterTheFileWasWritten()
    {
        var settings = SettingsData.LoadFromPath(PreferencesPath);
        var noticed = false;
        var livePublished = false;
        var diskPublished = false;
        void Changed()
        {
            noticed = true;
            livePublished = !settings.ThreeFingerDrag;
            diskPublished = !JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(PreferencesPath))!.ThreeFingerDrag;
        }
        SettingsData.PersistenceChanged += Changed;
        try
        {
            Assert.True(settings.TryCommit(candidate => candidate.ThreeFingerDrag = false).Success);
            Assert.True(noticed);
            Assert.True(livePublished);
            Assert.True(diskPublished);
        }
        finally { SettingsData.PersistenceChanged -= Changed; }
    }

    [Fact]
    public void DevicePageConstruction_IsPureAndPreservesExistingLargeValues()
    {
        App.SettingsData = SettingsData.LoadFromPath(PreferencesPath);
        var before = File.ReadAllBytes(PreferencesPath);
        var defaults = new MouseSpeedSettings(new(), new());
        Assert.Empty(App.SettingsData.ThreeFingerDeviceDragCursorConfigs);
        Assert.Equal(30, defaults.CursorSpeedProperty);
        Assert.Equal(10, defaults.CursorAccelerationProperty);
        Assert.Equal(before, File.ReadAllBytes(PreferencesPath));
        var existing = new MouseSpeedSettings(new(true, 450, 80), new());
        Assert.Equal(450, existing.CursorSpeedMaximum);
        Assert.Equal(80, existing.CursorAccelerationMaximum);
        Assert.Empty(App.SettingsData.ThreeFingerDeviceDragCursorConfigs);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void InvalidDeviceSpeed_DoesNotEnterRuntimeOrBreakSaving(float invalid)
    {
        App.SettingsData = SettingsData.LoadFromPath(PreferencesPath);
        var model = new MouseSpeedSettings(new(), new());
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;
        model.CursorSpeedProperty = invalid;
        Assert.Equal(30, model.CursorSpeedProperty);
        Assert.Empty(App.SettingsData.ThreeFingerDeviceDragCursorConfigs);
        Assert.Equal(1, changes);
        Assert.False(SettingsData.LastPersistenceResult!.Value.Success);
        App.SettingsData.save();
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void InvalidDeviceAcceleration_DoesNotEnterRuntime(float invalid)
    {
        App.SettingsData = SettingsData.LoadFromPath(PreferencesPath);
        var model = new MouseSpeedSettings(new(), new());
        model.CursorAccelerationProperty = invalid;
        Assert.Equal(10, model.CursorAccelerationProperty);
        Assert.Empty(App.SettingsData.ThreeFingerDeviceDragCursorConfigs);
    }

    [Fact]
    public void DeviceEdit_SavesImmediatelyAndFailedEditResetsTheModel()
    {
        App.SettingsData = SettingsData.LoadFromPath(PreferencesPath);
        var model = new MouseSpeedSettings(new(), new());
        model.CursorSpeedProperty = 60;
        Assert.Equal(60, model.CursorSpeedProperty);
        Assert.Equal(10, model.CursorAccelerationProperty);
        Assert.Equal(60, JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(PreferencesPath))!
            .ThreeFingerDeviceDragCursorConfigs["settings-test-device"].ThreeFingerDragCursorSpeed);
        using(var blocked = new FileStream(PreferencesPath, FileMode.Open, FileAccess.Read, FileShare.None))
            model.CursorSpeedProperty = 80;
        Assert.Equal(60, model.CursorSpeedProperty);
        Assert.Equal(60, App.SettingsData.ThreeFingerDeviceDragCursorConfigs["settings-test-device"].ThreeFingerDragCursorSpeed);
    }

    [Fact]
    public void DeviceBindingRefreshDuringPublication_DoesNotSubmitTheSameValueAgain()
    {
        App.SettingsData = SettingsData.LoadFromPath(PreferencesPath);
        var model = new MouseSpeedSettings(new(), new());
        var notifications = 0;
        void RefreshBindings()
        {
            notifications++;
            model.CursorSpeedProperty = model.CursorSpeedProperty;
            model.CursorAccelerationProperty = model.CursorAccelerationProperty;
            model.CursorMoveProperty = model.CursorMoveProperty;
        }
        SettingsData.PersistenceChanged += RefreshBindings;
        try
        {
            model.CursorSpeedProperty = 60;
            Assert.Equal(1, notifications);
            Assert.Equal(60, model.CursorSpeedProperty);
        }
        finally { SettingsData.PersistenceChanged -= RefreshBindings; }
    }

    [Fact]
    public void AtomicReplacementFailure_KeepsTheOldFileAndCleansTemporaryFile()
    {
        File.WriteAllText(PreferencesPath, "old-file");
        Assert.Throws<IOException>(() => AtomicSettingsFile.Write(PreferencesPath, "new-file",
            (_, _) => throw new IOException("injected publish failure")));
        Assert.Equal("old-file", File.ReadAllText(PreferencesPath));
        Assert.Equal(new[] { PreferencesPath }, Directory.GetFiles(_folder));
    }

    [Fact]
    public void ConcurrentCommits_AreSerializedWithoutLosingPreviousEdits()
    {
        var settings = SettingsData.LoadFromPath(PreferencesPath);
        Parallel.For(0, 20, index => Assert.True(settings.TryCommit(candidate =>
            candidate.ThreeFingerDeviceDragCursorConfigs["device-" + index] = new(true, 30 + index, 10)).Success));
        Assert.Equal(20, settings.ThreeFingerDeviceDragCursorConfigs.Count);
        Assert.Equal(20, JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(PreferencesPath))!
            .ThreeFingerDeviceDragCursorConfigs.Count);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Theory]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(0.5, false)]
    [InlineData(-1, false)]
    [InlineData(1001, false)]
    [InlineData(0, true)]
    [InlineData(1000, true)]
    [InlineData(32, true)]
    public void IntegerInput_RejectsEmptyNonFiniteAndFractionalValuesBeforeCasting(double value, bool valid)
    {
        Assert.Equal(valid, SettingsValueRules.TryInt(value, 0, 1000, out var result));
        if(valid) Assert.Equal((int)value, result);
    }
}
