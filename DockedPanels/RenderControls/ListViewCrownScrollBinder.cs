using ReaLTaiizor.Controls;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
    private static extern bool SetWindowPos(
      IntPtr hWnd,
      IntPtr hWndInsertAfter,
      int X,
      int Y,
      int cx,
      int cy,
      uint uFlags
    );

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

      // Guard: prevent recursion via ValueChanged when we update Value programmatically.
      private bool _suppressBarValueChanged;

      // Guard: prevent re-entrant Refresh/Disable when those calls themselves generate messages.
      private bool _inRefresh;

      // how many pixels per wheel "line" to move in LargeIcon view
      private readonly int _wheelPixelsPerLine;

      public Subclass(ListView lv, CrownScrollBar vBar)
      {
        _lv = lv;
        _vBar = vBar;

        _wheelPixelsPerLine = 16;

        AssignHandle(lv.Handle);

        DisableNativeBarsHard();
        WireBars();
        RefreshAll();

        _lv.Resize += (s, e) => RefreshAll();
        _lv.HandleDestroyed += (s, e) => ReleaseHandle();

        if (_vBar != null)
        {
          // Scroll via mouse wheel when hovering the Crown bar
          _vBar.MouseWheel += (s, e) =>
          {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0)
            {
              lines = 1;
            }

            int dir = Math.Sign(-e.Delta); // wheel up => content scrolls up (negative offset)
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
        var si = new SCROLLINFO
        {
          cbSize = Marshal.SizeOf<SCROLLINFO>(),
          fMask = SIF_ALL
        };
        GetScrollInfo(handle, bar, ref si);
        return si;
      }

      private void WireBars()
      {
        if (_vBar == null)
        {
          return;
        }

        _vBar.ScrollOrientation = ReaLTaiizor.Enum.Crown.ScrollOrientation.Vertical;

        _vBar.ValueChanged += (s, e) =>
        {
          if (_suppressBarValueChanged)
          {
            return;
          }

          int delta = _vBar.Value - _lastV;
          if (delta == 0)
          {
            return;
          }

          // Scroll the ListView vertically by delta pixels
          SendMessage(_lv.Handle, LVM_SCROLL, IntPtr.Zero, new IntPtr(delta));

          // Then resync Crown bar to whatever the ListView actually ended up at
          RefreshV();
        };
      }

      private void RefreshV()
      {
        if (_vBar == null)
        {
          return;
        }

        if (_inRefresh)
        {
          return;
        }

        _inRefresh = true;
        try
        {
          var si = ReadSI(_lv.Handle, SB_VERT);

          int contentRange = Math.Max(0, si.nMax - si.nMin + 1);
          int page = Math.Max(0, si.nPage);
          int pos = Math.Max(0, si.nPos);

          _vBar.Minimum = 0;

          if (contentRange <= 0)
          {
            _vBar.Maximum = 1;
            _vBar.ViewSize = 1;

            _suppressBarValueChanged = true;
            try
            {
              _lastV = 0;
              if (_vBar.Value != 0)
              {
                _vBar.Value = 0;
              }
            }
            finally
            {
              _suppressBarValueChanged = false;
            }

            _vBar.Visible = false;
            return;
          }

          _vBar.Maximum = contentRange;
          _vBar.ViewSize = Math.Min(_vBar.Maximum, page > 0 ? page : _vBar.Maximum);

          int maxPos = Math.Max(0, _vBar.Maximum - _vBar.ViewSize);
          int newValue = Math.Min(pos, maxPos);

          _suppressBarValueChanged = true;
          try
          {
            _lastV = newValue;
            if (_vBar.Value != newValue)
            {
              _vBar.Value = newValue;
            }
          }
          finally
          {
            _suppressBarValueChanged = false;
          }

          _vBar.Visible = (contentRange > page);
        }
        finally
        {
          _inRefresh = false;
        }
      }

      private void RefreshAll()
      {
        if (_inRefresh)
        {
          return;
        }

        _inRefresh = true;
        try
        {
          DisableNativeBarsSoft();
          RefreshV();
        }
        finally
        {
          _inRefresh = false;
        }
      }

      private void DisableNativeBarsSoft()
      {
        // called a lot – guard to avoid message feedback loops
        try
        {
          ShowScrollBar(_lv.Handle, SB_VERT, false);
        }
        catch
        {
        }

        try
        {
          ShowScrollBar(_lv.Handle, SB_HORZ, false);
        }
        catch
        {
        }
      }

      private void DisableNativeBarsHard()
      {
        try
        {
          int style = GetWindowLong(_lv.Handle, GWL_STYLE);
          int newStyle = style & ~(WS_VSCROLL | WS_HSCROLL);

          if (newStyle != style)
          {
            SetWindowLong(_lv.Handle, GWL_STYLE, newStyle);
            SetWindowPos(
              _lv.Handle,
              IntPtr.Zero,
              0,
              0,
              0,
              0,
              SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED
            );
          }
        }
        catch
        {
        }

        DisableNativeBarsSoft();
      }

      // Helper mimicking GET_WHEEL_DELTA_WPARAM with sign preserved
      private static int GetWheelDeltaFromWParam(IntPtr wParam)
      {
        long wp = wParam.ToInt64();
        int hiword = unchecked((int)((wp >> 16) & 0xFFFF));
        return (short)hiword;
      }

      protected override void WndProc(ref Message m)
      {
        // Intercept mouse wheel on the ListView itself
        if (m.Msg == WM_MOUSEWHEEL)
        {
          int delta = GetWheelDeltaFromWParam(m.WParam);
          if (delta != 0)
          {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0)
            {
              lines = 1;
            }

            int dir = Math.Sign(-delta);
            int pixels = dir * lines * _wheelPixelsPerLine;

            if (pixels != 0)
            {
              SendMessage(_lv.Handle, LVM_SCROLL, IntPtr.Zero, new IntPtr(pixels));
              RefreshV();
              return;
            }
          }
        }

        base.WndProc(ref m);

        // Only react to layout/scroll messages when we're *not* already in a refresh cycle
        if (_inRefresh)
        {
          return;
        }

        switch (m.Msg)
        {
          case WM_VSCROLL:
          case WM_MOUSEWHEEL:
          case WM_SIZE:
          case WM_WINDOWPOSCHANGED:
          case WM_NCCALCSIZE:
          case WM_STYLECHANGED:
          {
            RefreshAll();
            break;
          }

          case WM_HSCROLL:
          case WM_MOUSEHWHEEL:
          {
            // no horizontal bar in this layout; just keep native hidden
            DisableNativeBarsSoft();
            break;
          }
        }
      }
    }

    public static void Attach(ListView lv, CrownScrollBar vertical, CrownScrollBar horizontal = null)
    {
      if (lv.IsHandleCreated)
      {
        _ = new Subclass(lv, vertical);
      }
      else
      {
        lv.HandleCreated += (s, e) =>
        {
          _ = new Subclass(lv, vertical);
        };
      }
    }

    // Call this after repopulating items if needed
    public static void Nudge(ListView lv)
    {
      if (lv.IsHandleCreated)
      {
        SendMessage(lv.Handle, LVM_SCROLL, IntPtr.Zero, IntPtr.Zero);
      }
    }
  }

}
