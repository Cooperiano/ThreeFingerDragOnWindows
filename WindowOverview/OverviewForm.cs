using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace MissionControlPrototype;

internal enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down
}

internal sealed class OverviewForm : Form
{
    private const int OuterPadding = 54;
    private const int HeaderHeight = 78;
    private const int CellGap = 24;
    private const int CardPadding = 10;
    private const int TitleHeight = 38;
    private const int AnimationDurationMs = 230;

    private readonly List<WindowCard> _cards = [];
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly Stopwatch _animationClock = new();
    private int _selectedIndex;
    private int _draggedIndex = -1;
    private Point _dragOffset;
    private Point _dragStart;
    private bool _dragMoved;

    public event EventHandler? OverviewHidden;

    public OverviewForm()
    {
        Text = OverviewLanguage.Get("Touchpad Control — Window overview");
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(17, 20, 27);
        KeyPreview = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Cursor = Cursors.Default;

        _animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animationTimer.Tick += (_, _) => AnimateFrame();

        KeyDown += HandleKeyDown;
        MouseMove += HandleMouseMove;
        MouseDown += HandleMouseDown;
        MouseUp += HandleMouseUp;
        MouseCaptureChanged += HandleCaptureChanged;
        Deactivate += (_, _) =>
        {
            if (Visible)
            {
                HideOverview();
            }
        };
    }

    protected override bool ShowWithoutActivation => false;

    public void ShowOverview()
    {
        // Repeated open requests must not rebuild cards or lose the user's
        // selection/arrangement while the overview is already on screen.
        if (Visible) return;
        OverviewLanguage.Refresh();
        Text = OverviewLanguage.Get("Touchpad Control — Window overview");
        ReleaseThumbnails();
        Bounds = SystemInformation.VirtualScreen;
        _selectedIndex = 0;
        _draggedIndex = -1;

        Show();
        Activate();
        BringToFront();

        var windows = WindowCatalog.GetCurrentDesktopWindows();
        CreateCards(windows);
        RegisterThumbnails();

        _animationClock.Restart();
        _animationTimer.Start();
        Invalidate();
    }

    public void HideOverview()
    {
        _animationTimer.Stop();
        ClearPointerInteraction();
        Hide();
        ReleaseThumbnails();
        OverviewHidden?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearPointerInteraction();
            _animationTimer.Dispose();
            ReleaseThumbnails();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var backgroundBrush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(20, 24, 34),
            Color.FromArgb(8, 10, 15),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(backgroundBrush, ClientRectangle);

        using var headingFont = new Font("Segoe UI Variable Display", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var helperFont = new Font("Segoe UI Variable Text", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(245, 247, 252));
        using var mutedBrush = new SolidBrush(Color.FromArgb(155, 164, 180));

        graphics.DrawString(OverviewLanguage.Get("All windows"), headingFont, titleBrush, OuterPadding, 22);
        graphics.DrawString(OverviewLanguage.Get("S115"), helperFont, mutedBrush, OuterPadding, 52);

        if (_cards.Count == 0)
        {
            using var emptyFont = new Font("Segoe UI Variable Text", 18f, FontStyle.Regular, GraphicsUnit.Pixel);
            var emptyText = OverviewLanguage.Get("No windows to display on this desktop");
            var size = graphics.MeasureString(emptyText, emptyFont);
            graphics.DrawString(
                emptyText,
                emptyFont,
                mutedBrush,
                (ClientSize.Width - size.Width) / 2,
                (ClientSize.Height - size.Height) / 2);
            return;
        }

        for (var index = 0; index < _cards.Count; index++)
        {
            DrawCard(graphics, _cards[index], index == _selectedIndex);
        }
    }

    private void DrawCard(Graphics graphics, WindowCard card, bool selected)
    {
        var cardBounds = card.CurrentBounds;
        if (cardBounds.Width <= 2 || cardBounds.Height <= 2)
        {
            return;
        }

        var radius = 14f;
        using var path = RoundedRectangle(cardBounds, radius);
        using var cardBrush = new SolidBrush(selected
            ? Color.FromArgb(66, 77, 101)
            : Color.FromArgb(42, 48, 61));
        graphics.FillPath(cardBrush, path);

        using var borderPen = new Pen(selected
            ? Color.FromArgb(120, 180, 255)
            : Color.FromArgb(73, 82, 102), selected ? 3f : 1f);
        graphics.DrawPath(borderPen, path);

        var titleBounds = new Rectangle(
            cardBounds.Left + CardPadding,
            cardBounds.Bottom - TitleHeight,
            Math.Max(1, cardBounds.Width - CardPadding * 2),
            TitleHeight - 3);

        var iconLeft = titleBounds.Left;
        if (card.Window.Icon is not null)
        {
            graphics.DrawIcon(card.Window.Icon, new Rectangle(iconLeft, titleBounds.Top + 7, 20, 20));
            iconLeft += 28;
        }

        using var titleFont = new Font("Segoe UI Variable Text", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(235, 239, 247));
        var textBounds = new Rectangle(
            iconLeft,
            titleBounds.Top + 5,
            Math.Max(1, titleBounds.Right - iconLeft),
            24);
        TextRenderer.DrawText(
            graphics,
            card.Window.Title,
            titleFont,
            textBounds,
            titleBrush.Color,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void CreateCards(IReadOnlyList<WindowInfo> windows)
    {
        _cards.Clear();
        if (windows.Count == 0)
        {
            return;
        }

        var available = new Rectangle(
            OuterPadding,
            HeaderHeight + 18,
            Math.Max(200, ClientSize.Width - OuterPadding * 2),
            Math.Max(160, ClientSize.Height - HeaderHeight - OuterPadding));

        for (var index = 0; index < windows.Count; index++)
        {
            var source = windows[index].Bounds;
            source.Offset(-Left, -Top);
            _cards.Add(new WindowCard(windows[index], source, Rectangle.Empty));
        }
        // Keep CurrentBounds at the source window until the first animation
        // frame. Jumping straight to the target here caused a backwards flash.
        ApplyLayoutTargets(available, animate: true);
    }

    private void ApplyLayoutTargets(Rectangle available, bool animate)
    {
        var targets = OverviewLayoutEngine.Arrange(
            _cards.Select(card => card.Window.Bounds.Size).ToArray(),
            available,
            CellGap,
            CardPadding * 2,
            TitleHeight + CardPadding);

        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            card.StartBounds = card.CurrentBounds;
            card.TargetBounds = targets[index];
            if (!animate && index != _draggedIndex)
            {
                card.CurrentBounds = targets[index];
            }
        }

        if (animate)
        {
            _animationClock.Restart();
            _animationTimer.Start();
        }
    }

    private Rectangle AvailableLayoutBounds() => new(
        OuterPadding,
        HeaderHeight + 18,
        Math.Max(200, ClientSize.Width - OuterPadding * 2),
        Math.Max(160, ClientSize.Height - HeaderHeight - OuterPadding));

    private void RegisterThumbnails()
    {
        foreach (var card in _cards)
        {
            if (NativeMethods.DwmRegisterThumbnail(Handle, card.Window.Handle, out var thumbnail) == 0)
            {
                card.Thumbnail = thumbnail;
            }
        }

        UpdateThumbnails();
    }

    private void UpdateThumbnails()
    {
        foreach (var card in _cards)
        {
            if (card.Thumbnail == IntPtr.Zero)
            {
                continue;
            }

            var preview = new Rectangle(
                card.CurrentBounds.Left + CardPadding,
                card.CurrentBounds.Top + CardPadding,
                Math.Max(1, card.CurrentBounds.Width - CardPadding * 2),
                Math.Max(1, card.CurrentBounds.Height - TitleHeight - CardPadding));

            var properties = new NativeMethods.DwmThumbnailProperties
            {
                Flags = NativeMethods.DwmTnpRectDestination |
                        NativeMethods.DwmTnpOpacity |
                        NativeMethods.DwmTnpVisible |
                        NativeMethods.DwmTnpSourceClientAreaOnly,
                Destination = new NativeMethods.NativeRect
                {
                    Left = preview.Left,
                    Top = preview.Top,
                    Right = preview.Right,
                    Bottom = preview.Bottom
                },
                Opacity = 255,
                Visible = true,
                SourceClientAreaOnly = false
            };
            NativeMethods.DwmUpdateThumbnailProperties(card.Thumbnail, ref properties);
        }
    }

    private void ReleaseThumbnails()
    {
        foreach (var card in _cards)
        {
            if (card.Thumbnail != IntPtr.Zero)
            {
                NativeMethods.DwmUnregisterThumbnail(card.Thumbnail);
                card.Thumbnail = IntPtr.Zero;
            }

            card.Window.Icon?.Dispose();
        }

        _cards.Clear();
    }

    private void AnimateFrame()
    {
        var progress = Math.Clamp(_animationClock.Elapsed.TotalMilliseconds / AnimationDurationMs, 0d, 1d);
        var eased = 1d - Math.Pow(1d - progress, 3d);

        for (var index = 0; index < _cards.Count; index++)
        {
            if (index == _draggedIndex)
            {
                continue;
            }
            var card = _cards[index];
            card.CurrentBounds = OverviewGeometry.Interpolate(card.StartBounds, card.TargetBounds, eased);
        }

        UpdateThumbnails();
        Invalidate();

        if (progress >= 1d)
        {
            _animationTimer.Stop();
        }
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            HideOverview();
            return;
        }

        if (_cards.Count == 0)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Left:
                MoveSelection(NavigationDirection.Left);
                break;
            case Keys.Up:
                MoveSelection(NavigationDirection.Up);
                break;
            case Keys.Right:
                MoveSelection(NavigationDirection.Right);
                break;
            case Keys.Down:
                MoveSelection(NavigationDirection.Down);
                break;
            case Keys.Tab:
                _selectedIndex = (_selectedIndex + 1) % _cards.Count;
                Invalidate();
                break;
            case Keys.Enter:
                ActivateSelectedWindow();
                break;
        }
    }

    public void MoveSelection(NavigationDirection direction)
    {
        if (_cards.Count < 2)
        {
            return;
        }

        var current = Center(_cards[_selectedIndex].TargetBounds);
        var directionVector = direction switch
        {
            NavigationDirection.Left => new Point(-1, 0),
            NavigationDirection.Right => new Point(1, 0),
            NavigationDirection.Up => new Point(0, -1),
            _ => new Point(0, 1)
        };

        var directional = Enumerable.Range(0, _cards.Count)
            .Where(index => index != _selectedIndex)
            .Select(index =>
            {
                var candidate = Center(_cards[index].TargetBounds);
                var dx = candidate.X - current.X;
                var dy = candidate.Y - current.Y;
                var forward = dx * directionVector.X + dy * directionVector.Y;
                var sideways = Math.Abs(dx * directionVector.Y - dy * directionVector.X);
                return new { Index = index, Forward = forward, Sideways = sideways };
            })
            .Where(candidate => candidate.Forward > 0)
            .OrderBy(candidate => candidate.Forward + candidate.Sideways * 2.5)
            .ThenBy(candidate => candidate.Sideways)
            .FirstOrDefault();

        if (directional is not null)
        {
            _selectedIndex = directional.Index;
        }
        else
        {
            var candidates = Enumerable.Range(0, _cards.Count)
                .Where(index => index != _selectedIndex)
                .Select(index => new { Index = index, Center = Center(_cards[index].TargetBounds) });
            _selectedIndex = direction switch
            {
                NavigationDirection.Left => candidates.OrderByDescending(item => item.Center.X).ThenBy(item => Math.Abs(item.Center.Y - current.Y)).First().Index,
                NavigationDirection.Right => candidates.OrderBy(item => item.Center.X).ThenBy(item => Math.Abs(item.Center.Y - current.Y)).First().Index,
                NavigationDirection.Up => candidates.OrderByDescending(item => item.Center.Y).ThenBy(item => Math.Abs(item.Center.X - current.X)).First().Index,
                _ => candidates.OrderBy(item => item.Center.Y).ThenBy(item => Math.Abs(item.Center.X - current.X)).First().Index
            };
        }

        Invalidate();
    }

    public void ActivateSelectedWindow()
    {
        if (_cards.Count > 0)
        {
            ActivateWindow(_cards[_selectedIndex]);
        }
    }

    private static Point Center(Rectangle rectangle) =>
        new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggedIndex >= 0)
        {
            _dragMoved |= Math.Abs(e.X - _dragStart.X) > 6 || Math.Abs(e.Y - _dragStart.Y) > 6;
            // A click's small jitter must not move its own hit rectangle to
            // follow the pointer, otherwise an out-of-card release always hits.
            if (!_dragMoved) return;
            var dragged = _cards[_draggedIndex];
            dragged.CurrentBounds = new Rectangle(
                e.X - _dragOffset.X,
                e.Y - _dragOffset.Y,
                dragged.CurrentBounds.Width,
                dragged.CurrentBounds.Height);
            if (_dragMoved && _cards.Count > 1)
            {
                var nearestIndex = Enumerable.Range(0, _cards.Count)
                    .Where(index => index != _draggedIndex)
                    .OrderBy(index => DistanceSquared(e.Location, Center(_cards[index].TargetBounds)))
                    .First();
                if (nearestIndex != _draggedIndex &&
                    _cards[nearestIndex].TargetBounds.Contains(e.Location))
                {
                    OverviewLayoutEngine.MoveItem(_cards, _draggedIndex, nearestIndex);
                    _draggedIndex = nearestIndex;
                    _selectedIndex = nearestIndex;
                    ApplyLayoutTargets(AvailableLayoutBounds(), animate: true);
                }
            }

            UpdateThumbnails();
            Invalidate();
            return;
        }

        // During movement the DWM previews and card frames can overlap. Wait
        // for the settled, non-overlapping layout before accepting a new hit.
        if (_animationTimer.Enabled) return;
        UpdatePointerSelection(e.Location);
    }

    private void UpdatePointerSelection(Point point)
    {
        var index = HitTest(point);
        if (index >= 0 && index != _selectedIndex)
        {
            _selectedIndex = index;
            Invalidate();
        }
    }

    private int HitTest(Point point) =>
        OverviewGeometry.HitTest(_cards, point, static card => card.CurrentBounds);

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _animationTimer.Enabled)
        {
            return;
        }

        var index = HitTest(e.Location);
        if (index >= 0)
        {
            _selectedIndex = index;
            _draggedIndex = index;
            _dragOffset = new Point(
                e.X - _cards[index].CurrentBounds.Left,
                e.Y - _cards[index].CurrentBounds.Top);
            _dragStart = e.Location;
            _dragMoved = false;
            Capture = true;
            Invalidate();
        }
    }

    private void HandleMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _draggedIndex < 0)
        {
            return;
        }

        var index = _draggedIndex;
        var card = _cards[index];
        var moved = _dragMoved;
        ClearPointerInteraction();
        if (!moved)
        {
            // Moving just outside a card by less than the drag threshold is
            // a cancelled click, not permission to open the original target.
            if (OverviewGeometry.IsClickRelease(index, moved, HitTest(e.Location))) ActivateWindow(card);
            return;
        }

        ApplyLayoutTargets(AvailableLayoutBounds(), animate: true);
        Invalidate();
    }

    private void ClearPointerInteraction()
    {
        // Clear state before releasing capture; the synchronous capture-lost
        // event must never turn cancellation into a click or reorder.
        _draggedIndex = -1;
        _dragMoved = false;
        Capture = false;
    }

    private void HandleCaptureChanged(object? sender, EventArgs e)
    {
        if (Capture || _draggedIndex < 0) return;
        ClearPointerInteraction();
        if (Visible && _cards.Count > 0)
            ApplyLayoutTargets(AvailableLayoutBounds(), animate: true);
    }

    private static long DistanceSquared(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (long)dx * dx + (long)dy * dy;
    }

    private void ActivateWindow(WindowCard card)
    {
        HideOverview();
        WindowActivator.Activate(card.Window.Handle);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class WindowCard(
        WindowInfo window,
        Rectangle startBounds,
        Rectangle targetBounds)
    {
        public WindowInfo Window { get; } = window;
        public Rectangle StartBounds { get; set; } = startBounds;
        public Rectangle TargetBounds { get; set; } = targetBounds;
        public Rectangle CurrentBounds { get; set; } = startBounds;
        public IntPtr Thumbnail { get; set; }
    }
}
