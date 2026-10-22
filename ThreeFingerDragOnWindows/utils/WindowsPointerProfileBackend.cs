#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.TaskScheduler;
using ThreeFingerDragOnWindows.gestures;
using TouchpadControl.Pointer;

namespace ThreeFingerDragOnWindows.utils;

/// <summary>Read/modify/read native OS values, never synthesize cursor motion.</summary>
internal sealed class WindowsPointerProfileBackend : IPointerProfileBackend
{
    private const uint GetMouse = 3, SetMouse = 4, GetSpeed = 0x70, SetSpeed = 0x71;
    private const uint GetTouchpad = 0xAE, SetTouchpad = 0xAF, NotifyAndPersist = 3;
    internal string Transport { get; private set; } = "unobserved";

    internal string ReadUnrelatedTouchpadSettings()
    {
        if(!TryGetTouchpad(out var parameters)) return "legacy-unavailable";
        return $"{parameters.UserFlags}/{parameters.Sensitivity}/{parameters.FeedbackIntensity}/" +
               $"{parameters.ClickForce}/{parameters.RightZoneWidth}/{parameters.RightZoneHeight}";
    }

    // SDK 10.0.26100 WinUser.h, TOUCHPAD_PARAMETERS_V1: 44 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct TouchpadParameters
    {
        public uint Version, Contacts, LegacyFeatures, StatusFlags, UserFlags;
        public uint Sensitivity, CursorSpeed, FeedbackIntensity, ClickForce, RightZoneWidth, RightZoneHeight;
    }

    private static bool TryGetTouchpad(out TouchpadParameters parameters)
    {
        parameters = new TouchpadParameters { Version = 1 };
        return TouchpadInfo(GetTouchpad, (uint)Marshal.SizeOf<TouchpadParameters>(), ref parameters, 0);
    }

    public PointerAccelerationProfile Read()
    {
        if(!TryGetTouchpad(out var touchpad)) return Broker(null);
        if((touchpad.StatusFlags & 1) == 0) throw new InvalidOperationException("No Precision Touchpad is present.");
        var mouse = new int[3];
        var speed = 0;
        Check(MouseInfo(GetMouse, 0, mouse, 0));
        Check(ReadSpeed(GetSpeed, 0, ref speed, 0));
        Transport = "native-SPI-touchpad-v1";
        return new(speed, mouse[0], mouse[1], mouse[2], checked((int)touchpad.CursorSpeed));
    }

    public void Apply(PointerAccelerationProfile profile)
    {
        if(profile != profile.Normalize()) throw new ArgumentOutOfRangeException(nameof(profile));
        if(!TryGetTouchpad(out var touchpad)) { Broker(profile); return; }
        if((touchpad.StatusFlags & 1) == 0) throw new InvalidOperationException("No Precision Touchpad is present.");
        Check(MouseInfo(SetMouse, 0,
            new[] { profile.Threshold1, profile.Threshold2, profile.AccelerationLevel }, NotifyAndPersist));
        Check(WriteSpeed(SetSpeed, 0, new IntPtr(profile.PointerSpeed), NotifyAndPersist));
        // Preserve all unrelated user settings; change only cursor speed.
        touchpad.CursorSpeed = (uint)profile.TouchpadCursorSpeed;
        Check(TouchpadInfo(SetTouchpad, (uint)Marshal.SizeOf<TouchpadParameters>(), ref touchpad, NotifyAndPersist));
        Transport = "native-SPI-touchpad-v1";
    }

    private static void Check(bool success)
    {
        if(!success) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private PointerAccelerationProfile Broker(PointerAccelerationProfile? requested)
    {
        var request = Guid.NewGuid().ToString("N");
        var taskName = "Touchpad Control Pointer Request " + request;
        var receiptPath = Path.Combine(RuntimeDiagnostics.Folder, "pointer-request-" + request + ".json");
        Directory.CreateDirectory(RuntimeDiagnostics.Folder);
        using var service = new TaskService();
        RunningTask? running = null;
        try
        {
            var definition = service.NewTask();
            definition.RegistrationInfo.Description = "Verified native pointer settings; temporary compatibility request.";
            definition.Principal.RunLevel = TaskRunLevel.LUA;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = TimeSpan.FromSeconds(10);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                BuildBrokerScript(request, receiptPath, requested)));
            var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            definition.Actions.Add(new ExecAction(powerShell,
                "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded));
            using var registered = service.RootFolder.RegisterTaskDefinition(taskName, definition,
                TaskCreation.CreateOrUpdate, WindowsIdentity.GetCurrent().Name, null, TaskLogonType.InteractiveToken);
            running = registered.Run();
            var timer = Stopwatch.StartNew();
            while(timer.Elapsed < TimeSpan.FromSeconds(12))
            {
                if(File.Exists(receiptPath))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(receiptPath));
                    var root = document.RootElement;
                    if(root.GetProperty("Request").GetString() != request)
                        throw new IOException("Pointer receipt belongs to a different request.");
                    if(!root.GetProperty("Success").GetBoolean())
                        throw new IOException(root.GetProperty("Error").GetString());
                    var profile = new PointerAccelerationProfile(
                        root.GetProperty("Speed").GetInt32(), root.GetProperty("Threshold1").GetInt32(),
                        root.GetProperty("Threshold2").GetInt32(), root.GetProperty("Acceleration").GetInt32(),
                        root.GetProperty("TouchpadSpeed").GetInt32());
                    if(profile != profile.Normalize()) throw new IOException("Invalid pointer readback.");
                    if(requested.HasValue && profile != requested.Value)
                        throw new IOException("Compatibility readback differs from requested values.");
                    Transport = "verified-legacy-broker";
                    return profile;
                }
                Thread.Sleep(50); // Transaction worker only, never the UI thread.
            }
            throw new TimeoutException("Pointer request did not return a verified receipt.");
        }
        finally
        {
            // Expired requests must not overwrite a later rollback.
            try { running?.Stop(); } catch { }
            running?.Dispose();
            try { service.RootFolder.DeleteTask(taskName, false); } catch { }
            try { File.Delete(receiptPath); File.Delete(receiptPath + ".tmp"); } catch { }
        }
    }

    private static string BuildBrokerScript(string request, string receiptPath, PointerAccelerationProfile? requested)
    {
        var profile = requested ?? PointerAccelerationProfile.MacLike;
        var apply = requested.HasValue ? "$true" : "$false";
        var path = receiptPath.Replace("'", "''");
        return $$"""
            $ErrorActionPreference = 'Stop'
            $result = @{Request='{{request}}'; Success=$false; Error=''; Speed=0; Threshold1=0; Threshold2=0; Acceleration=0; TouchpadSpeed=0}
            try {
                Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            public static class PointerNative {
                [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", ExactSpelling=true, SetLastError=true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool Mouse(uint a, uint b, int[] c, uint d);
                [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", ExactSpelling=true, SetLastError=true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool ReadSpeed(uint a, uint b, ref int c, uint d);
                [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", ExactSpelling=true, SetLastError=true)]
                [return: MarshalAs(UnmanagedType.Bool)]
                public static extern bool WriteSpeed(uint a, uint b, IntPtr c, uint d);
            }
            '@
                if({{apply}}) {
                    if(-not [PointerNative]::Mouse(4,0,[int[]]({{profile.Threshold1}},{{profile.Threshold2}},{{profile.AccelerationLevel}}),3)) {throw 'SetMouse failed'}
                    if(-not [PointerNative]::WriteSpeed(0x71,0,[IntPtr]{{profile.PointerSpeed}},3)) {throw 'SetSpeed failed'}
                    Set-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad' -Name CursorSpeed -Type DWord -Value {{profile.TouchpadCursorSpeed}}
                }
                $mouse = [int[]](0,0,0)
                $speed = 0
                if(-not [PointerNative]::Mouse(3,0,$mouse,0)) {throw 'GetMouse failed'}
                if(-not [PointerNative]::ReadSpeed(0x70,0,[ref]$speed,0)) {throw 'GetSpeed failed'}
                $result.Speed=$speed; $result.Threshold1=$mouse[0]; $result.Threshold2=$mouse[1]; $result.Acceleration=$mouse[2]
                $result.TouchpadSpeed = [int](Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad' -Name CursorSpeed)
                $result.Success=$true
            } catch {$result.Error=$_.Exception.Message}
            $result | ConvertTo-Json -Compress | Set-Content -LiteralPath '{{path}}.tmp' -Encoding UTF8
            Move-Item -LiteralPath '{{path}}.tmp' -Destination '{{path}}' -Force
            """;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MouseInfo(uint action, uint parameter, int[] value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadSpeed(uint action, uint parameter, ref int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteSpeed(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TouchpadInfo(uint action, uint parameter, ref TouchpadParameters value, uint flags);
}
