using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SwimEditor
{

  /// <summary>
  /// Custom owner-painted vertical scrollbar matching the dark editor theme.
  /// Auto-hides when content fits; no OS drawing, so the track never renders white.
  /// - Keyboard support (Up/Down/PageUp/PageDown/Home/End)
  /// - Mouse wheel on both content and host
  /// - Safe hook/unhook of events to avoid leaks
  /// </summary>
  public class DarkScrollBar : Control
  {

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int EM_LINESCROLL = 0x00B6;

    public event Action<int> ScrollValueChanged;

    public int Minimum { get; private set; } = 0;
    public int Maximum { get; private set; } = 0; // inclusive
    public int LargeChange { get; private set; } = 1;
    public int SmallChange { get; private set; } = 1;

    /// <summary>
    /// When true (default), the control hides itself (Visible=false) if no scrolling is needed.
    /// </summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>
    /// True when there is more content than the visible range (i.e. scrolling is possible).
    /// </summary>
    public bool NeedsScroll
    {
      get { return MaximumRangeCount > LargeChange; }
    }

    private int _value = 0;
    public int Value
    {
      get { return _value; }
      set
      {
        int clamped = Clamp(value, Minimum, Math.Max(Minimum, Maximum - LargeChange + 1));
        if (clamped != _value)
        {
          _value = clamped;
          Invalidate();
          if (ScrollValueChanged != null) ScrollValueChanged(_value);
        }
      }
    }

    private bool dragging = false;
    private int dragOffsetY = 0;

    // Stored hooks for safe unhooking
    private Control hookedControl;
    private Control hookedHost;
    private Action syncCallback;

    private MouseEventHandler handlerControlWheel;
    private MouseEventHandler handlerHostWheel;

    private EventHandler handlerVisibleChanged;
    private EventHandler handlerSizeChanged;
    private EventHandler handlerHostSizeChanged;

    public DarkScrollBar()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer |
               ControlStyles.UserPaint |
               ControlStyles.ResizeRedraw, true);

      Cursor = Cursors.Hand;
      TabStop = true; // allow keyboard
      BackColor = SwimEditorTheme.PageBg;
    }

    public int MaximumRangeCount
    {
      get { return Maximum - Minimum + 1; }
    }

    public void SetRange(int minimum, int maximumInclusive, int largeChange, int smallChange)
    {
      Minimum = minimum;
      Maximum = Math.Max(minimum, maximumInclusive);
      LargeChange = Math.Max(1, largeChange);
      SmallChange = Math.Max(1, smallChange);

      // Keep Value valid and repaint
      Value = _value;
      Invalidate();

      // Auto-hide if content fits
      if (AutoHide)
        Visible = NeedsScroll;
    }

    /// <summary>
    /// Hooks mouse wheel events for both the control and its host panel
    /// so scrolling works even when focus isn't on the scrollbar.
    /// </summary>
    public void SetScrollHooks(Control control, Control controlHost, Action syncCallback)
    {
      UnhookScrollHooks();

      if (control == null || controlHost == null || syncCallback == null)
        return;

      hookedControl = control;
      hookedHost = controlHost;
      this.syncCallback = syncCallback;

      handlerControlWheel = (s, e) =>
      {
        int notches = e.Delta / 120;
        int perNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        int lines = -notches * perNotch; // invert: wheel up = scroll up
        SendMessage(control.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
        syncCallback();
      };

      handlerHostWheel = (s, e) =>
      {
        int notches = e.Delta / 120;
        int perNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        int lines = -notches * perNotch;
        SendMessage(control.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
        syncCallback();
      };

      control.MouseWheel += handlerControlWheel;
      controlHost.MouseWheel += handlerHostWheel;
    }

    /// <summary>
    /// Keeps the host's right padding equal to the scrollbar width only when the bar is visible.
    /// Call once after constructing the layout: vbar.HookAutoHideLayout(controlHost);
    /// </summary>
    public void HookAutoHideLayout(Control host)
    {
      UnhookAutoHideLayout();

      if (host == null) return;

      void Apply()
      {
        var p = host.Padding;
        int right = this.Visible ? this.Width : 0;
        host.Padding = new Padding(p.Left, p.Top, right, p.Bottom);
      }

      handlerVisibleChanged = (s, e) => Apply();
      handlerSizeChanged = (s, e) => Apply();
      handlerHostSizeChanged = (s, e) => Apply();

      this.VisibleChanged += handlerVisibleChanged;
      this.SizeChanged += handlerSizeChanged;
      host.SizeChanged += handlerHostSizeChanged;

      // Initial apply
      Apply();

      hookedHost = host;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      // If auto-hidden or no need to scroll, nothing to paint
      if (!NeedsScroll && AutoHide)
        return;

      var g = e.Graphics;
      var cr = ClientRectangle;

      using (var track = new SolidBrush(SwimEditorTheme.PageBg))
        g.FillRectangle(track, cr);

      using (var border = new Pen(SwimEditorTheme.Line))
        g.DrawRectangle(border, new Rectangle(0, 0, cr.Width - 1, cr.Height - 1));

      Rectangle thumb = GetThumbRect();
      using (var thumbBrush = new SolidBrush(SwimEditorTheme.Bg))
        g.FillRectangle(thumbBrush, thumb);

      using (var thumbBorder = new Pen(SwimEditorTheme.Line))
        g.DrawRectangle(thumbBorder, new Rectangle(thumb.X, thumb.Y, thumb.Width - 1, thumb.Height - 1));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
      base.OnMouseDown(e);
      if (e.Button != MouseButtons.Left) return;
      if (!NeedsScroll) return;

      Rectangle thumb = GetThumbRect();
      if (thumb.Contains(e.Location))
      {
        dragging = true;
        dragOffsetY = e.Y - thumb.Y;
        Capture = true;
      }
      else
      {
        if (e.Y < thumb.Y) Value = Value - Math.Max(1, LargeChange - 1);
        else Value = Value + Math.Max(1, LargeChange - 1);
      }
      Focus(); // enable keyboard after click
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
      base.OnMouseMove(e);
      if (!dragging || !NeedsScroll) return;

      int trackTop = 2;
      int trackHeight = Math.Max(1, Height - 4);
      int thumbHeight = GetThumbHeight(trackHeight);

      int y = e.Y - dragOffsetY;
      y = Clamp(y, trackTop, trackTop + trackHeight - thumbHeight);

      float t = (trackHeight - thumbHeight) <= 0 ? 0f :
                (float)(y - trackTop) / (float)(trackHeight - thumbHeight);

      int maxFirst = Math.Max(Minimum, Maximum - LargeChange + 1);
      int newVal = Minimum + (int)Math.Round(t * (maxFirst - Minimum));
      Value = newVal;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
      base.OnMouseUp(e);
      if (dragging)
      {
        dragging = false;
        Capture = false;
      }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
      base.OnMouseWheel(e);
      if (!NeedsScroll) return;

      int notches = e.Delta / 120;
      int linesPerNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
      int deltaLines = -notches * linesPerNotch;

      int newValue = Value + deltaLines;
      Value = Clamp(newValue, Minimum, Math.Max(Minimum, Maximum - LargeChange + 1));

      if (ScrollValueChanged != null)
        ScrollValueChanged(Value);

      Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
      switch (keyData)
      {
        case Keys.Up:
        case Keys.Down:
        case Keys.PageUp:
        case Keys.PageDown:
        case Keys.Home:
        case Keys.End:
          return true;
      }
      return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
      base.OnKeyDown(e);
      if (!NeedsScroll) return;

      int v = Value;
      switch (e.KeyCode)
      {
        case Keys.Up: v -= SmallChange; break;
        case Keys.Down: v += SmallChange; break;
        case Keys.PageUp: v -= Math.Max(1, LargeChange - 1); break;
        case Keys.PageDown: v += Math.Max(1, LargeChange - 1); break;
        case Keys.Home: v = Minimum; break;
        case Keys.End: v = Math.Max(Minimum, Maximum - LargeChange + 1); break;
      }
      Value = v;
      if (ScrollValueChanged != null)
        ScrollValueChanged(Value);
      e.Handled = true;
    }

    private Rectangle GetThumbRect()
    {
      int trackTop = 2;
      int trackLeft = 2;
      int trackWidth = Math.Max(1, Width - 4);
      int trackHeight = Math.Max(1, Height - 4);

      int thumbHeight = GetThumbHeight(trackHeight);
      int maxFirst = Math.Max(Minimum, Maximum - LargeChange + 1);
      float range = Math.Max(1, maxFirst - Minimum);

      int y = trackTop;
      if (range > 0)
      {
        float t = (float)(Value - Minimum) / range;
        y = trackTop + (int)Math.Round(t * (trackHeight - thumbHeight));
      }

      return new Rectangle(trackLeft, y, trackWidth, thumbHeight);
    }

    private int GetThumbHeight(int trackHeight)
    {
      float visible = Math.Max(1, LargeChange);
      float total = Math.Max(visible, MaximumRangeCount);
      int h = (int)Math.Round(trackHeight * (visible / total));
      return Math.Max(18, Math.Min(trackHeight, h));
    }

    private static int Clamp(int v, int a, int b)
    {
      if (v < a) return a;
      if (v > b) return b;
      return v;
    }

    /// <summary>
    /// Unhooks previously attached wheel hooks.
    /// </summary>
    public void UnhookScrollHooks()
    {
      if (hookedControl != null && handlerControlWheel != null)
        hookedControl.MouseWheel -= handlerControlWheel;

      if (hookedHost != null && handlerHostWheel != null)
        hookedHost.MouseWheel -= handlerHostWheel;

      hookedControl = null;
      handlerControlWheel = null;

      // keep hookedHost if used by auto-hide layout; only clear wheel handler
      handlerHostWheel = null;
    }

    /// <summary>
    /// Unhooks the auto-hide layout handlers.
    /// </summary>
    public void UnhookAutoHideLayout()
    {
      if (hookedHost != null)
      {
        if (handlerHostSizeChanged != null)
          hookedHost.SizeChanged -= handlerHostSizeChanged;
      }

      if (handlerVisibleChanged != null)
        this.VisibleChanged -= handlerVisibleChanged;

      if (handlerSizeChanged != null)
        this.SizeChanged -= handlerSizeChanged;

      handlerVisibleChanged = null;
      handlerSizeChanged = null;
      handlerHostSizeChanged = null;
      // do not null out hookedHost here if it is still used for wheel hooks
    }

    /// <summary>
    /// Unhooks everything (wheel + layout). Call from parent Dispose().
    /// </summary>
    public void UnhookAll()
    {
      UnhookScrollHooks();
      UnhookAutoHideLayout();
      hookedHost = null;
      syncCallback = null;
      ScrollValueChanged = null;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        UnhookAll();
      }
      base.Dispose(disposing);
    }

  } // class DarkScrollBar

} // Namespace SwimEditor
