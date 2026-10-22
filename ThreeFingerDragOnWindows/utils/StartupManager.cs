using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Microsoft.Win32.TaskScheduler;
using TouchpadControl;
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace ThreeFingerDragOnWindows.utils;

public sealed record StartupStatus(bool Exists, bool CanonicalEnabled, bool ActionValid, bool Elevated,
    bool LegacyElevatedEnabled, bool LegacyPackageEnabled)
{
    public bool Enabled => CanonicalEnabled || LegacyElevatedEnabled || LegacyPackageEnabled;
    public bool HasLegacyEnabled => LegacyElevatedEnabled || LegacyPackageEnabled;
}

/// <summary>The settings UI and local installer share one stable, per-user login task.</summary>
public static class StartupManager
{
    public static bool IsSuiteManaged => File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Stats Desktop", "suite-host.txt"));
    private const string LegacyTaskPath = @"\ThreeFingerDragOnWindows\Run on Startup";
    private const string PackageTaskId = "ThreeFingerDragOnWindows";
    private static readonly SemaphoreSlim ChangeGate = new(1, 1);
    internal static bool IsBusy => ChangeGate.CurrentCount == 0;
    internal static bool LastChangeFailed { get; private set; }
    internal static void RecordFailure(Exception exception)
    {
        LastChangeFailed = true;
        RuntimeDiagnostics.Write("startup-change", exception.ToString());
    }
    public static string RegisteredAlias => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "ThreeFingerDragOnWindows.exe");

    public static async Task<StartupStatus> GetStatusAsync()
    {
        StartupTask packageTask = await StartupTask.GetAsync(PackageTaskId);
        StartupTaskState packageState = packageTask.State;
        return await System.Threading.Tasks.Task.Run(() => ReadStatus(packageState));
    }

    public static async System.Threading.Tasks.Task SetStartupAsync(bool enabled, bool elevated, System.Action afterVerified = null)
    {
        if(IsSuiteManaged) throw new InvalidOperationException("Sign-in startup is managed by Seelen Desktop Suite.");
        if(!await ChangeGate.WaitAsync(0)) throw new InvalidOperationException("A startup change is already in progress.");
        LastChangeFailed = false;
        try
        {
            StartupTask packageTask = await StartupTask.GetAsync(PackageTaskId);
            StartupTaskState packageBefore = packageTask.State;
            Snapshot before = await System.Threading.Tasks.Task.Run(Capture);
            try
            {
                // Only an explicit settings operation retires the old entries. Queries are read-only.
                if(IsPackageEnabled(packageTask.State))
                {
                    packageTask.Disable();
                    if(IsPackageEnabled(packageTask.State))
                        throw new InvalidOperationException("Windows policy prevented disabling the duplicate package startup entry.");
                }
                await System.Threading.Tasks.Task.Run(() => ApplyCanonical(enabled, elevated));
                StartupStatus observed = await GetStatusAsync();
                if(observed.Enabled != enabled || observed.HasLegacyEnabled
                    || (enabled && (!observed.CanonicalEnabled || !observed.ActionValid || observed.Elevated != elevated)))
                    throw new InvalidOperationException("The startup change could not be independently verified.");
                afterVerified?.Invoke();
                RuntimeDiagnostics.Write("startup-change", $"enabled={enabled}; elevated={elevated}; verified=true");
            }
            catch(Exception failure)
            {
                Exception rollbackFailure = null;
                try { await System.Threading.Tasks.Task.Run(() => Restore(before)); }
                catch(Exception exception) { rollbackFailure = exception; }
                try
                {
                    if(IsPackageEnabled(packageBefore) && !IsPackageEnabled(packageTask.State))
                    {
                        StartupTaskState restored = await packageTask.RequestEnableAsync();
                        if(!IsPackageEnabled(restored)) throw new InvalidOperationException("The previous package startup state could not be restored.");
                    }
                    else if(!IsPackageEnabled(packageBefore) && IsPackageEnabled(packageTask.State)) packageTask.Disable();
                }
                catch(Exception exception) { rollbackFailure ??= exception; }
                if(rollbackFailure != null)
                    throw new AggregateException("Startup change failed; restoring the previous state also failed.", failure, rollbackFailure);
                throw;
            }
        }
        catch(Exception exception) { RecordFailure(exception); throw; }
        finally { ChangeGate.Release(); }
    }

    public static bool IsElevatedStartupOn()
    {
        using var service = new TaskService();
        using ScheduledTask task = service.GetTask("\\" + StartupRegistrationPolicy.TaskName);
        return task != null && task.Enabled && task.Definition.Principal.RunLevel == TaskRunLevel.Highest;
    }

    private static bool IsPackageEnabled(StartupTaskState state) =>
        state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    private static StartupStatus ReadStatus(StartupTaskState packageState)
    {
        using var service = new TaskService();
        using ScheduledTask task = service.GetTask("\\" + StartupRegistrationPolicy.TaskName);
        using ScheduledTask legacy = service.GetTask(LegacyTaskPath);
        var action = task?.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
        bool valid = task != null && StartupRegistrationPolicy.IsActionValid(action?.Path, action?.Arguments,
            RegisteredAlias, task.Definition.Actions.Count, HasCurrentUserLogonTrigger(task.Definition))
            && IsCurrentUserTask(task.Definition);
        return new StartupStatus(task != null, task?.Enabled == true, valid,
            task?.Definition.Principal.RunLevel == TaskRunLevel.Highest, legacy?.Enabled == true,
            IsPackageEnabled(packageState));
    }

    private static bool IsCurrentUserTask(TaskDefinition definition)
    {
        return definition.Principal.LogonType == TaskLogonType.InteractiveToken
            && IsCurrentUserAccount(definition.Principal.UserId);
    }

    private static bool IsCurrentUserAccount(string principal)
    {
        if(string.IsNullOrWhiteSpace(principal)) return false;
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if(string.Equals(principal, identity.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(principal, identity.User?.Value, StringComparison.OrdinalIgnoreCase)) return true;
        // Task Scheduler may return an unqualified local account name rather than DOMAIN\user.
        try { return identity.User?.Equals(new NTAccount(principal).Translate(typeof(SecurityIdentifier))) == true; }
        catch(IdentityNotMappedException) { return false; }
    }

    private static bool HasCurrentUserLogonTrigger(TaskDefinition definition)
    {
        LogonTrigger[] enabledTriggers = definition.Triggers.OfType<LogonTrigger>().Where(trigger => trigger.Enabled).ToArray();
        return enabledTriggers.Length > 0 && enabledTriggers.All(trigger => IsCurrentUserAccount(trigger.UserId));
    }

    private static void ApplyCanonical(bool enabled, bool elevated)
    {
        using var service = new TaskService();
        using ScheduledTask legacy = service.GetTask(LegacyTaskPath);
        if(legacy?.Enabled == true) legacy.Enabled = false;
        using ScheduledTask existing = service.GetTask("\\" + StartupRegistrationPolicy.TaskName);
        if(!enabled)
        {
            if(existing != null) existing.Enabled = false;
            return;
        }
        if(elevated && !Utils.IsAppRunningAsAdministrator())
            throw new UnauthorizedAccessException("Changing elevated startup requires administrator approval.");
        if(existing != null && !IsCurrentUserTask(existing.Definition))
            throw new InvalidOperationException("The existing startup task belongs to a different principal or logon mode; it was not replaced.");
        TaskDefinition definition = existing?.Definition ?? service.NewTask();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        definition.RegistrationInfo.Description = "Start Touchpad Control quietly at sign-in and recover unexpected exits.";
        definition.Principal.UserId = identity.Name;
        definition.Principal.LogonType = TaskLogonType.InteractiveToken;
        definition.Principal.RunLevel = elevated ? TaskRunLevel.Highest : TaskRunLevel.LUA;
        definition.Actions.Clear();
        definition.Actions.Add(new ExecAction(RegisteredAlias, StartupRegistrationPolicy.Arguments));
        if(!definition.Triggers.OfType<LogonTrigger>().Any())
            definition.Triggers.Add(new LogonTrigger { UserId = identity.Name });
        else
            foreach(LogonTrigger trigger in definition.Triggers.OfType<LogonTrigger>())
            {
                trigger.UserId = identity.Name;
                trigger.Enabled = true;
            }
        definition.Settings.Enabled = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        definition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        service.RootFolder.RegisterTaskDefinition(StartupRegistrationPolicy.TaskName, definition,
            TaskCreation.CreateOrUpdate, identity.Name, null, TaskLogonType.InteractiveToken).Dispose();
    }

    private sealed record Snapshot(string CanonicalXml, bool CanonicalEnabled, bool LegacyEnabled);
    private static Snapshot Capture()
    {
        using var service = new TaskService();
        using ScheduledTask task = service.GetTask("\\" + StartupRegistrationPolicy.TaskName);
        using ScheduledTask legacy = service.GetTask(LegacyTaskPath);
        if(task != null && !IsCurrentUserTask(task.Definition))
            throw new InvalidOperationException("The existing startup task belongs to a different principal or logon mode; it was not changed.");
        if(legacy != null && !IsCurrentUserTask(legacy.Definition))
            throw new InvalidOperationException("The legacy startup entry belongs to a different principal or logon mode; it was not changed.");
        return new Snapshot(task?.Xml, task?.Enabled == true, legacy?.Enabled == true);
    }

    private static void Restore(Snapshot before)
    {
        using var service = new TaskService();
        using ScheduledTask current = service.GetTask("\\" + StartupRegistrationPolicy.TaskName);
        if(before.CanonicalXml != null)
        {
            if(current == null || current.Enabled != before.CanonicalEnabled || !XmlEquals(current.Xml, before.CanonicalXml))
            {
                TaskDefinition definition = service.NewTask();
                definition.XmlText = before.CanonicalXml;
                definition.Settings.Enabled = before.CanonicalEnabled;
                service.RootFolder.RegisterTaskDefinition(StartupRegistrationPolicy.TaskName, definition,
                    TaskCreation.CreateOrUpdate, definition.Principal.UserId, null, definition.Principal.LogonType).Dispose();
            }
        }
        else if(current != null)
        {
            service.RootFolder.DeleteTask(StartupRegistrationPolicy.TaskName, false);
        }
        using ScheduledTask legacy = service.GetTask(LegacyTaskPath);
        if(legacy != null && legacy.Enabled != before.LegacyEnabled) legacy.Enabled = before.LegacyEnabled;
        using ScheduledTask verified = service.GetTask("\\" + StartupRegistrationPolicy.TaskName);
        using ScheduledTask verifiedLegacy = service.GetTask(LegacyTaskPath);
        if((before.CanonicalXml == null ? verified != null
            : verified == null || verified.Enabled != before.CanonicalEnabled || !XmlEquals(verified.Xml, before.CanonicalXml))
            || (verifiedLegacy?.Enabled == true) != before.LegacyEnabled)
            throw new InvalidOperationException("The restored startup task could not be independently verified.");
    }

    private static bool XmlEquals(string left, string right) =>
        XNode.DeepEquals(XDocument.Parse(left), XDocument.Parse(right));
}
