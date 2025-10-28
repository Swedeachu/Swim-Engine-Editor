using ReaLTaiizor.Controls;
using System.Runtime.InteropServices;

namespace SwimEditor
{
  static class ListViewCrownScrollBinder
  {

    // Win32 & ListView messages
    private const int WM_VSCROLL = 0x0115;
    private const int WM_HSCROLL = 0x0114;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int WM_SIZE = 0x0005;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_STYLECHANGED = 0x007D;

    private const int LVM_FIRST = 0x1000;
    private const int LVM_SCROLL = LVM_FIRST + 20;

    private const int SB_HORZ = 0;
    private const int SB_VERT = 1;

    private const int SIF_RANGE = 0x1;
    private const int SIF_PAGE = 0x2;
    private const int SIF_POS = 0x4;
    private const int SIF_TRACKPOS = 0x10;
    private const int SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS | SIF_TRACKPOS;

    // Window style surgery to prevent native bar flash
    private const int GWL_STYLE = -16;
    private const int WS_VSCROLL = 0x00200000;
    private const int WS_HSCROLL = 0x00100000;

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                            int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
      public int cbSize;
      public int fMask;
      public int nMin;
      public int nMax;
      public int nPage;
      public int nPos;
      public int nTrackPos;
    }

    private class Subclass : NativeWindow
    {

      private readonly ListView _lv;
      private readonly CrownScrollBar _vBar;
      private int _lastV;

      // how many pixels per wheel "line" to move in LargeIcon view (tweak if you want)
      private readonly int _wheelPixelsPerLine;

      public Subclass(ListView lv, CrownScrollBar vBar)
      {
        _lv = lv;
        _vBar = vBar;

        // 16–24 usually feels OK with 48px thumbnails; tie to image size if you like.
        _wheelPixelsPerLine = 16;

        AssignHandle(lv.Handle);

        // Hide & strip native scrollbars right away
        DisableNativeBarsHard();
        WireBars();
        RefreshAll();

        _lv.Resize += (s, e) => RefreshAll();
        _lv.HandleDestroyed += (s, e) => ReleaseHandle();

        // If the mouse wheel happens while hovering our custom vBar, scroll the ListView.
        if (_vBar != null)
        {
          _vBar.MouseWheel += (s, e) =>
          {
            int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            int dir = Math.Sign(-e.Delta); // wheel up = negative scroll (content up)
            int pixels = dir * lines * _wheelPixelsPerLine;

            if (pixels != 0)
            {
              SendMessage(_lv.Handle, LVM_SCROLL, IntPtr.Zero, new IntPtr(pixels));
              RefreshV();
            }
          };
        }
      }

      private static SCROLLINFO ReadSI(IntPtr handle, int bar)
      {
        var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
        GetScrollInfo(handle, bar, ref si);
        return si;
      }

      private void WireBars()
      {
        if (_vBar != null)
        {
          // ensure vertical orientation (default is vertical, but be explicit)
          _vBar.ScrollOrientation = 0;

          _vBar.ValueChanged += (s, e) =>
          {
            int delta = _vBar.Value - _lastV;
            if (delta != 0)
            {
              // vertical pixel scroll
              SendMessage(_lv.Handle, LVM_SCROLL, IntPtr.Zero, new IntPtr(delta));
              _lastV = _vBar.Value;
              RefreshV();
            }
          };
        }
      }

      private void RefreshV()
      {
        if (_vBar == null) return;

        var si = ReadSI(_lv.Handle, SB_VERT);

        // Map native SCROLLINFO -> CrownScrollBar model cleanly
        // CrownScrollBar needs: Minimum, Maximum (content), ViewSize (viewport), Value (pos)
        // Win32 gives us: nMin/nMax (pos range), nPage (page size), nPos (current)
        int contentRange = Math.Max(0, si.nMax - si.nMin + 1);
        int page = Math.Max(0, si.nPage);
        int pos = Math.Max(0, si.nPos);

        _vBar.Minimum = 0;
        _vBar.Maximum = contentRange; // total content "units"
        _vBar.ViewSize = page;        // visible "units" (affects thumb size)

        _lastV = Math.Min(pos, Math.Max(0, contentRange - page));
        if (_vBar.Value != _lastV)
          _vBar.Value = _lastV;

        // hide bar if no scrollable range
        _vBar.Visible = (contentRange > page);
      }

      private void RefreshAll()
      {
        DisableNativeBarsSoft();
        RefreshV();
      }

      private void DisableNativeBarsSoft()
      {
        // Redo every time to avoid flash when ListView relayouts
        try { ShowScrollBar(_lv.Handle, SB_VERT, false); } catch { }
        try { ShowScrollBar(_lv.Handle, SB_HORZ, false); } catch { }
      }

      private void DisableNativeBarsHard()
      {
        // Remove WS_VSCROLL/WS_HSCROLL so Windows can’t resurrect them (prevents flash)
        try
        {
          int style = GetWindowLong(_lv.Handle, GWL_STYLE);
          int newStyle = style & ~(WS_VSCROLL | WS_HSCROLL);
          if (newStyle != style)
          {
            SetWindowLong(_lv.Handle, GWL_STYLE, newStyle);
            SetWindowPos(_lv.Handle, IntPtr.Zero, 0, 0, 0, 0,
              SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
          }
        }
        catch { }

        DisableNativeBarsSoft();
      }

      // Helper to mirror GET_WHEEL_DELTA_WPARAM safely on x86/x64
      private static int GetWheelDeltaFromWParam(IntPtr wParam)
      {
        // HIWORD of WPARAM, preserved sign
        long wp = wParam.ToInt64();
        int hiword = unchecked((int)((wp >> 16) & 0xFFFF));
        return (short)hiword; // cast to short to keep the sign, then widen to int
      }

      protected override void WndProc(ref Message m)
      {
        // Intercept wheel and drive ListView ourselves when needed.
        if (m.Msg == WM_MOUSEWHEEL)
        {
          int delta = GetWheelDeltaFromWParam(m.WParam);   // +120/-120 per notch typically
          if (delta != 0)
          {
            int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            int dir = Math.Sign(-delta);                   // wheel up => scroll content up
            int pixels = dir * lines * _wheelPixelsPerLine;

            if (pixels != 0)
            {
              SendMessage(_lv.Handle, LVM_SCROLL, IntPtr.Zero, new IntPtr(pixels));
              RefreshV();
              return; // eat it; we handled the scroll
            }
          }
        }

        base.WndProc(ref m);

        // Whenever the ListView changes layout or scrolls, keep bars hidden & synced.
        switch (m.Msg)
        {
          case WM_VSCROLL:
          case WM_MOUSEWHEEL:
          case WM_SIZE:
          case WM_WINDOWPOSCHANGED:
          case WM_NCCALCSIZE:
          case WM_STYLECHANGED:
            DisableNativeBarsSoft();
            RefreshV();
            break;

          case WM_HSCROLL:
          case WM_MOUSEHWHEEL:
            // no horizontal bar in this layout; just keep native hidden
            DisableNativeBarsSoft();
            break;
        }
      }

    } // Subclass

    public static void Attach(ListView lv, CrownScrollBar vertical, CrownScrollBar horizontal = null)
    {
      if (lv.IsHandleCreated)
      {
        _ = new Subclass(lv, vertical);
      }
      else
      {
        lv.HandleCreated += (s, e) => _ = new Subclass(lv, vertical);
      }
    }

    // Call this after you repopulate items if needed:
    public static void Nudge(ListView lv)
    {
      // a harmless nudge forces SCROLLINFO to recalc
      if (lv.IsHandleCreated)
        SendMessage(lv.Handle, LVM_SCROLL, IntPtr.Zero, IntPtr.Zero);
    }

  } // class ListViewCrownScrollBinder

} // Namespace SwimEditor
