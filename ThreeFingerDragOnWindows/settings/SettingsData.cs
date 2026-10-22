#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;
using ThreeFingerDragOnWindows.utils;
using TouchpadControl.Settings;

namespace ThreeFingerDragOnWindows.settings;

public sealed class SettingsData
{
    private static readonly object PersistenceGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string? _loadedPath;
    private string? _preservedLoadError;
    private string? _persistedSnapshot;

    public static bool DidVersionChanged { get; set; }
    public static SettingsCommitResult? LastPersistenceResult { get; private set; }
    public static event Action? PersistenceChanged;
    [JsonIgnore] public bool IsPersistenceProtected => _preservedLoadError is not null;
    public int SettingsVersion { get; set; }

    public bool ThreeFingerDrag { get; set; } = true;
    public enum ThreeFingerDragButtonType { NONE, LEFT, RIGHT, MIDDLE }
    public ThreeFingerDragButtonType ThreeFingerDragButton { get; set; } = ThreeFingerDragButtonType.LEFT;
    public bool ThreeFingerDragAllowReleaseAndRestart { get; set; } = true;
    public int ThreeFingerDragReleaseDelay { get; set; } = 500;

    public sealed class ThreeFingerDragConfig
    {
        public ThreeFingerDragConfig() { }
        public ThreeFingerDragConfig(bool cursorMoveProperty, float cursorSpeedProperty, float cursorAccelerationProperty)
        {
            ThreeFingerDragCursorMove = cursorMoveProperty;
            ThreeFingerDragCursorSpeed = cursorSpeedProperty;
            ThreeFingerDragCursorAcceleration = cursorAccelerationProperty;
        }
        public bool ThreeFingerDragCursorMove { get; set; } = true;
        public float ThreeFingerDragCursorSpeed { get; set; } = 30;
        public float ThreeFingerDragCursorAcceleration { get; set; } = 10;
    }

    public Dictionary<string, ThreeFingerDragConfig> ThreeFingerDeviceDragCursorConfigs { get; set; } = new();
    // Keep legacy values on disk when their obsolete controls are hidden.
    public int ThreeFingerDragCursorAveraging { get; set; } = 1;
    public int ThreeFingerDragMaxFingerMoveDistance { get; set; }
    public int ThreeFingerDragStartThreshold { get; set; } = 32;
    public int ThreeFingerDragStopThreshold { get; set; } = 10;
    public bool FourFingerGestures { get; set; } = true;
    public int FourFingerSwipeThreshold { get; set; } = 90;

    // New users must explicitly choose a Windows profile. Existing true is retained.
    public bool MacLikePointerTuning { get; set; }
    public bool PointerBackupCaptured { get; set; }
    public int PointerBackupSpeed { get; set; } = 10;
    public int PointerBackupThreshold1 { get; set; } = 6;
    public int PointerBackupThreshold2 { get; set; } = 10;
    public int PointerBackupAccelerationLevel { get; set; } = 1;
    public int PointerBackupTouchpadSpeed { get; set; } = 10;
    public string Language { get; set; } = "system";
    public enum StartupActionType
    {
        NONE, ENABLE_ELEVATED_RUN_WITH_STARTUP, DISABLE_ELEVATED_RUN_WITH_STARTUP,
        ENABLE_ELEVATED_STARTUP, DISABLE_ELEVATED_STARTUP
    }
    public StartupActionType StartupAction { get; set; }
    public bool RunElevated { get; set; }
    public bool RecordLogs { get; set; }

    public static SettingsData load() => LoadFromPath(GetPreferencesPath());

    // Tests use an explicit isolated path and never resolve the user's LocalState.
    public static SettingsData LoadFromPath(string path)
    {
        Logger.Log("Loading settings...");
        string contents;
        try { contents = File.ReadAllText(path); }
        catch(FileNotFoundException) { return CreateNew(path); }
        catch(DirectoryNotFoundException) { return CreateNew(path); }
        catch(Exception error) { return ProtectOriginal(path, error); }
        SettingsData settings;
        try
        {
            settings = JsonSerializer.Deserialize<SettingsData>(contents)
                ?? throw new JsonException("The settings document cannot be null.");
            settings._loadedPath = path;
            var originalVersion = settings.SettingsVersion;
            settings.ThreeFingerDeviceDragCursorConfigs ??= new();
            settings.Validate();
            settings._persistedSnapshot = JsonSerializer.Serialize(settings, JsonOptions);
            var migration = SettingsMigration.Apply(originalVersion, settings.FourFingerSwipeThreshold,
                settings.ThreeFingerDragStartThreshold);
            settings.SettingsVersion = migration.Version;
            settings.FourFingerSwipeThreshold = migration.FourFingerThreshold;
            settings.ThreeFingerDragStartThreshold = migration.StartDragThreshold;
            if(migration.RequiresSave)
            {
                DidVersionChanged = true;
                // Parameter migration must not silently change system startup tasks.
                settings.save();
            }
            Logger.Log($"Settings loaded, version = {settings.SettingsVersion}");
            return settings;
        }
        catch(Exception error)
        {
            return ProtectOriginal(path, error);
        }
    }

    private static SettingsData ProtectOriginal(string path, Exception error)
    {
        // A safe runtime fallback is not permission to replace an unreadable file.
        RuntimeDiagnostics.Write("settings-load", error.ToString());
        var settings = new SettingsData
        {
            SettingsVersion = SettingsMigration.CurrentVersion,
            ThreeFingerDrag = false,
            FourFingerGestures = false,
            _loadedPath = path,
            _preservedLoadError = error.Message
        };
        Report(SettingsCommitResult.Failed("Saved preferences could not be loaded and were preserved. " + error.Message));
        return settings;
    }

    private static SettingsData CreateNew(string path)
    {
        var settings = new SettingsData { SettingsVersion = SettingsMigration.CurrentVersion, _loadedPath = path };
        DidVersionChanged = true;
        try { settings.save(); }
        catch(Exception error) { RuntimeDiagnostics.Write("settings-create", error.ToString()); }
        return settings;
    }

    public SettingsCommitResult TryCommit(Action<SettingsData> edit)
    {
        SettingsCommitResult result;
        lock(PersistenceGate)
        {
            result = SettingsCommitTransaction.TryCommit(this, current => current.CloneForEdit(), edit,
                candidate => candidate.Validate(), candidate => candidate.Persist(), out var committed);
            if(result.Success)
            {
                foreach(var property in typeof(SettingsData).GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    if(property.SetMethod?.IsPublic == true) property.SetValue(this, property.GetValue(committed));
                _persistedSnapshot = committed._persistedSnapshot;
            }
        }
        Report(result);
        return result;
    }

    public static SettingsCommitResult RejectEdit(string error)
    {
        var result = SettingsCommitResult.Failed(error);
        Report(result);
        return result;
    }

    public void save()
    {
        try
        {
            lock(PersistenceGate) Persist();
            Report(SettingsCommitResult.Saved);
        }
        catch(Exception error)
        {
            Report(SettingsCommitResult.Failed(error.Message));
            throw;
        }
    }

    private SettingsData CloneForEdit()
    {
        EnsureWritable();
        var clone = JsonSerializer.Deserialize<SettingsData>(JsonSerializer.Serialize(this, JsonOptions))!;
        clone._loadedPath = _loadedPath;
        clone._persistedSnapshot = _persistedSnapshot;
        return clone;
    }

    private void Persist()
    {
        EnsureWritable();
        Validate();
        var previousVersion = SettingsVersion;
        SettingsVersion = SettingsMigration.CurrentVersion;
        try
        {
            var contents = JsonSerializer.Serialize(this, JsonOptions);
            // Reopening/hiding a normal v9 page must not rewrite its original bytes.
            if(contents != _persistedSnapshot)
                AtomicSettingsFile.Write(_loadedPath ?? GetPreferencesPath(), contents);
            _persistedSnapshot = contents;
        }
        catch
        {
            SettingsVersion = previousVersion;
            throw;
        }
    }

    private void EnsureWritable()
    {
        if(_preservedLoadError is not null)
            throw new InvalidOperationException("Saved preferences are protected after a load failure. " + _preservedLoadError);
        if(SettingsVersion > SettingsMigration.CurrentVersion)
            throw new InvalidOperationException("Preferences were saved by a newer application version.");
    }

    private void Validate()
    {
        if(SettingsVersion < 0 || SettingsVersion > SettingsMigration.CurrentVersion)
            throw new InvalidDataException("Unsupported settings version.");
        Check(ThreeFingerDragReleaseDelay, 0, SettingsValueRules.ReleaseDelayMaximum, nameof(ThreeFingerDragReleaseDelay));
        Check(ThreeFingerDragCursorAveraging, SettingsValueRules.AveragingMinimum,
            SettingsValueRules.AveragingMaximum, nameof(ThreeFingerDragCursorAveraging));
        Check(ThreeFingerDragMaxFingerMoveDistance, 0, SettingsValueRules.MaximumMoveDistance, nameof(ThreeFingerDragMaxFingerMoveDistance));
        Check(ThreeFingerDragStartThreshold, 0, SettingsValueRules.DragThresholdMaximum, nameof(ThreeFingerDragStartThreshold));
        Check(ThreeFingerDragStopThreshold, 0, SettingsValueRules.DragThresholdMaximum, nameof(ThreeFingerDragStopThreshold));
        Check(FourFingerSwipeThreshold, SettingsValueRules.FourFingerThresholdMinimum,
            SettingsValueRules.FourFingerThresholdMaximum, nameof(FourFingerSwipeThreshold));
        if(!Enum.IsDefined(ThreeFingerDragButton) || !Enum.IsDefined(StartupAction))
            throw new InvalidDataException("Invalid settings choice.");
        if(ThreeFingerDeviceDragCursorConfigs is null) throw new InvalidDataException("Missing device settings.");
        foreach(var config in ThreeFingerDeviceDragCursorConfigs.Values)
        {
            if(config is null) throw new InvalidDataException("Invalid device settings.");
            Check(config.ThreeFingerDragCursorSpeed, SettingsValueRules.CursorSpeedMinimum,
                SettingsValueRules.CursorSpeedMaximum, nameof(config.ThreeFingerDragCursorSpeed));
            Check(config.ThreeFingerDragCursorAcceleration, SettingsValueRules.CursorAccelerationMinimum,
                SettingsValueRules.CursorAccelerationMaximum, nameof(config.ThreeFingerDragCursorAcceleration));
        }
        if(PointerBackupCaptured)
        {
            Check(PointerBackupSpeed, 1, 20, nameof(PointerBackupSpeed));
            Check(PointerBackupThreshold1, 0, 20, nameof(PointerBackupThreshold1));
            Check(PointerBackupThreshold2, 0, 40, nameof(PointerBackupThreshold2));
            Check(PointerBackupAccelerationLevel, 0, 2, nameof(PointerBackupAccelerationLevel));
            Check(PointerBackupTouchpadSpeed, 1, 20, nameof(PointerBackupTouchpadSpeed));
        }
    }

    private static void Check(double value, double minimum, double maximum, string name)
    {
        if(!SettingsValueRules.IsFiniteInRange(value, minimum, maximum))
            throw new InvalidDataException("Invalid value for " + name + ".");
    }

    private static void Report(SettingsCommitResult result)
    {
        LastPersistenceResult = result;
        var listeners = PersistenceChanged;
        if(listeners is null) return;
        // Subscriber failure must not relabel a completed disk write as a rollback.
        foreach(Action listener in listeners.GetInvocationList())
            try { listener(); }
            catch(Exception error) { RuntimeDiagnostics.Write("settings-notification", error.ToString()); }
    }

    private static string GetPreferencesPath()
    {
        string folder;
        try { folder = ApplicationData.Current.LocalFolder.Path; }
        catch
        {
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ThreeFingerDragOnWindowsPreview");
        }
        return Path.Combine(folder, "preferences.json");
    }
}
