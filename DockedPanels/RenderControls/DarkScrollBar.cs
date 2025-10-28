using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SwimEditor
{

  /// <summary>
  /// Custom owner-painted vertical scrollbar matching the dark editor theme.
  /// Auto-hides when content fits; no OS drawing, so the track never renders white.
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

    public DarkScrollBar()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer |
               ControlStyles.UserPaint |
               ControlStyles.ResizeRedraw, true);

      Cursor = Cursors.Hand;
      TabStop = false;
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
      if (control == null || controlHost == null || syncCallback == null)
        return;

      // Wheel on the control -> scroll by system notch amount
      control.MouseWheel += (s, e) =>
      {
        // 120 = one notch. Positive delta = wheel up.
        int notches = e.Delta / 120;
        int perNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        int lines = -notches * perNotch; // invert: wheel up = scroll up

        SendMessage(control.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
        syncCallback();
      };

      // Also support wheel when hovering the empty padding area (right of log)
      controlHost.MouseWheel += (s, e) =>
      {
        int notches = e.Delta / 120;
        int perNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        int lines = -notches * perNotch;

        SendMessage(control.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
        syncCallback();
      };
    }

    /// <summary>
    /// Keeps the host's right padding equal to the scrollbar width only when the bar is visible.
    /// Call once after constructing the layout: vbar.HookAutoHideLayout(controlHost);
    /// </summary>
    public void HookAutoHideLayout(Control host)
    {
      if (host == null) return;

      void Apply()
      {
        var p = host.Padding;
        int right = this.Visible ? this.Width : 0;
        host.Padding = new Padding(p.Left, p.Top, right, p.Bottom);
      }

      // Update when visibility changes or when size/width changes
      this.VisibleChanged += (s, e) => Apply();
      this.SizeChanged += (s, e) => Apply();
      host.SizeChanged += (s, e) => Apply();

      // Initial apply
      Apply();
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

      // Determine direction and amount
      int notches = e.Delta / 120;
      int linesPerNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
      int deltaLines = -notches * linesPerNotch;

      // Apply movement to scrollbar value
      int newValue = Value + deltaLines;
      Value = Clamp(newValue, Minimum, Math.Max(Minimum, Maximum - LargeChange + 1));

      if (ScrollValueChanged != null)
        ScrollValueChanged(Value);

      Invalidate();
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

  } // class DarkVScrollBar

} // Namespace SwimEditor
