namespace MissionControlPrototype;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        OverviewLanguage.Refresh();
        var gestureMode = args.Any(argument =>
            string.Equals(argument, "--gesture", StringComparison.OrdinalIgnoreCase));
        var companionMode = args.Any(argument =>
            string.Equals(argument, "--companion", StringComparison.OrdinalIgnoreCase));
        using var context = new MissionControlContext(gestureMode, showTrayIcon: !companionMode);
        if (gestureMode)
        {
            EventHandler? showOnIdle = null;
            showOnIdle = (_, _) =>
            {
                Application.Idle -= showOnIdle;
                context.ShowFromGesture();
            };
            Application.Idle += showOnIdle;
        }
        Application.Run(context);
    }
}

internal sealed class MissionControlContext : ApplicationContext
{
    private const int ToggleHotkeyId = 0x4D43;
    private const int GestureLeftHotkeyId = 0x4D44;
    private const int GestureRightHotkeyId = 0x4D45;
    private const int GestureUpHotkeyId = 0x4D46;
    private const int GestureDownHotkeyId = 0x4D47;
    private const int GestureActivateHotkeyId = 0x4D48;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSpace = 0x20;
    private const uint VkLeft = 0x25;
    private const uint VkUp = 0x26;
    private const uint VkRight = 0x27;
    private const uint VkDown = 0x28;
    private const uint VkReturn = 0x0D;

    private static readonly int[] RegisteredHotkeyIds =
    [
        ToggleHotkeyId,
        GestureLeftHotkeyId,
        GestureRightHotkeyId,
        GestureUpHotkeyId,
        GestureDownHotkeyId,
        GestureActivateHotkeyId
    ];

    private readonly HotkeyWindow _hotkeyWindow;
    private readonly NotifyIcon _trayIcon;
    private OverviewForm? _overview;

    private readonly bool _gestureMode;

    public MissionControlContext(bool gestureMode = false, bool showTrayIcon = true)
    {
        _gestureMode = gestureMode;
        _hotkeyWindow = new HotkeyWindow(HandleHotkey);
        if (!gestureMode && !NativeMethods.RegisterHotKey(
                _hotkeyWindow.Handle,
                ToggleHotkeyId,
                ModControl | ModAlt | ModNoRepeat,
                VkSpace))
        {
            MessageBox.Show(
                OverviewLanguage.Get("Unable to register Ctrl+Alt+Space. Another app may be using it."),
                "Touchpad Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        if (!gestureMode)
        {
            var gestureModifiers = ModControl | ModAlt | ModShift | ModNoRepeat;
            NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, GestureLeftHotkeyId, gestureModifiers, VkLeft);
            NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, GestureRightHotkeyId, gestureModifiers, VkRight);
            NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, GestureUpHotkeyId, gestureModifiers, VkUp);
            NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, GestureDownHotkeyId, gestureModifiers, VkDown);
            NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, GestureActivateHotkeyId, gestureModifiers, VkReturn);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(OverviewLanguage.Get("Open overview"), null, (_, _) => ShowOverview());
        menu.Items.Add(OverviewLanguage.Get("Quit App"), null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Touchpad Control — Ctrl+Alt+Space",
            Visible = !gestureMode && showTrayIcon,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ToggleOverview();
    }

    public void ShowFromGesture()
    {
        _overview ??= new OverviewForm();
        _overview.OverviewHidden += (_, _) => ExitThread();
        _overview.ShowOverview();
    }

    private void ToggleOverview()
    {
        if (_overview is { Visible: true })
        {
            _overview.HideOverview();
            return;
        }

        ShowOverview();
    }

    private void ShowOverview()
    {
        _overview ??= new OverviewForm();
        _overview.ShowOverview();
    }

    private void HandleHotkey(int hotkeyId)
    {
        if (OverviewLifecycle.IsCommand(hotkeyId))
        {
            switch (OverviewLifecycle.Resolve(hotkeyId, _overview is { Visible: true }))
            {
                case OverviewLifecycleAction.Open:
                    ShowOverview();
                    break;
                case OverviewLifecycleAction.Hide:
                    _overview?.HideOverview();
                    break;
                case OverviewLifecycleAction.ShowDesktop:
                    NativeMethods.SendKeyboardChord(0x5B, 0x44);
                    break;
            }
            return;
        }
        if (hotkeyId == ToggleHotkeyId)
        {
            ToggleOverview();
            return;
        }

        if (_overview is { Visible: true })
        {
            switch (hotkeyId)
            {
                case GestureLeftHotkeyId:
                    _overview.MoveSelection(NavigationDirection.Left);
                    break;
                case GestureRightHotkeyId:
                    _overview.MoveSelection(NavigationDirection.Right);
                    break;
                case GestureUpHotkeyId:
                    _overview.MoveSelection(NavigationDirection.Up);
                    break;
                case GestureDownHotkeyId:
                    _overview.MoveSelection(NavigationDirection.Down);
                    break;
                case GestureActivateHotkeyId:
                    _overview.ActivateSelectedWindow();
                    break;
            }
            return;
        }

        switch (hotkeyId)
        {
            case GestureUpHotkeyId:
                ShowOverview();
                break;
            case GestureLeftHotkeyId:
                NativeMethods.SendKeyboardChord(0x5B, 0x11, 0x27); // Win+Ctrl+Right: next desktop.
                break;
            case GestureRightHotkeyId:
                NativeMethods.SendKeyboardChord(0x5B, 0x11, 0x25); // Win+Ctrl+Left: previous desktop.
                break;
            case GestureDownHotkeyId:
                NativeMethods.SendKeyboardChord(0x5B, 0x44); // Win+D: show desktop.
                break;
        }
    }

    protected override void ExitThreadCore()
    {
        if (!_gestureMode)
        {
            foreach (var hotkeyId in RegisteredHotkeyIds)
            {
                NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, hotkeyId);
            }
        }
        _overview?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _hotkeyWindow.Dispose();
        base.ExitThreadCore();
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private static readonly uint CommandMessage = NativeMethods.RegisterWindowMessage(
            "TouchpadControl.MissionControl.Gesture.v1");
        private readonly Action<int> _onHotkey;

        public HotkeyWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams
            {
                Caption = "MissionControlPrototype.HotkeyWindow",
                Parent = NativeMethods.HwndMessage
            });
        }

        protected override void WndProc(ref Message message)
        {
            if (CommandMessage != 0 && message.Msg == CommandMessage)
            {
                var command = message.WParam.ToInt32();
                if (message.LParam == new IntPtr(1) &&
                    (RegisteredHotkeyIds.Contains(command) || OverviewLifecycle.IsCommand(command)))
                {
                    try
                    {
                        _onHotkey(command);
                        message.Result = new IntPtr(1);
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Trace.WriteLine(exception);
                        message.Result = IntPtr.Zero;
                    }
                }
                else message.Result = IntPtr.Zero;
                return;
            }
            if (message.Msg == WmHotkey)
            {
                _onHotkey(message.WParam.ToInt32());
            }

            base.WndProc(ref message);
        }

        public void Dispose() => DestroyHandle();
    }
}
