using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WeifenLuo.WinFormsUI.Docking;
using Timer = System.Windows.Forms.Timer;

namespace SwimEditor
{

  public class GameViewDock : DockContent
  {

    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    private Panel renderSurface;
    private Process engineProcess;
    private IntPtr engineChildHwnd = IntPtr.Zero;

    // P/Invoke for focus and child enumeration
    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    public GameViewDock()
    {
      renderSurface = new DoubleBufferedPanel
      {
        Dock = DockStyle.Fill,
        BackColor = Color.Black
      };

      Controls.Add(renderSurface);
      ShowHint = DockState.Document;

      // keep the child filling the panel (important for hit-testing)
      renderSurface.Resize += (_, __) =>
      {
        if (engineChildHwnd != IntPtr.Zero)
        {
          MoveWindow(engineChildHwnd, 0, 0, renderSurface.ClientSize.Width, renderSurface.ClientSize.Height, false);
        }
      };

      Shown += (_, __) => StartEngineIfNeeded();
      FormClosed += (_, __) => StopEngineIfNeeded();
    }

    public IntPtr RenderHandle => renderSurface.Handle;

    private void StartEngineIfNeeded()
    {
      if (engineProcess != null && !engineProcess.HasExited) return;

      // Ensure the panel has a valid HWND
      var _ = RenderHandle;

      string exeDir = Application.StartupPath;
      string exePath = Path.Combine(exeDir, "Swim Engine.exe");

      if (!File.Exists(exePath))
      {
        MessageBox.Show($"Could not find engine binary:\n{exePath}", "Swim Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
      }

      var psi = new ProcessStartInfo
      {
        FileName = exePath,
        Arguments = $"--parent-hwnd {RenderHandle}",
        WorkingDirectory = exeDir,
        UseShellExecute = false
      };

      try
      {
        engineProcess = Process.Start(psi);

        // Poll for the child window (created by the engine with class name "SwimEngine")
        var t = new Timer { Interval = 50 };
        int tries = 0;
        t.Tick += (s, e) =>
        {
          tries++;
          CaptureEngineChildHandle();
          if (engineChildHwnd != IntPtr.Zero || tries > 120) // ~6s max
          {
            ((Timer)s).Stop();
            AfterChildDiscovered();
          }
        };
        t.Start();
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to launch Swim Engine:\n{ex.Message}", "Swim Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private void CaptureEngineChildHandle()
    {
      engineChildHwnd = IntPtr.Zero;

      EnumChildWindows(RenderHandle, (hwnd, l) =>
      {
        var sb = new StringBuilder(256);
        if (GetClassName(hwnd, sb, sb.Capacity) > 0)
        {
          // Must match engine windowClassName (L"SwimEngine")
          if (sb.ToString() == "SwimEngine")
          {
            engineChildHwnd = hwnd;
            return false; // stop
          }
        }
        return true; // continue
      }, IntPtr.Zero);
    }

    private void AfterChildDiscovered()
    {
      if (engineChildHwnd == IntPtr.Zero) return;

      BringWindowToTop(engineChildHwnd);
      SetWindowPos(engineChildHwnd, HWND_TOP, 0, 0, renderSurface.ClientSize.Width, renderSurface.ClientSize.Height, 0);
      MoveWindow(engineChildHwnd, 0, 0, renderSurface.ClientSize.Width, renderSurface.ClientSize.Height, false);

      // Give the panel a reference to forward messages
      if (renderSurface is DoubleBufferedPanel panel)
      {
        panel.SetChildWindow(engineChildHwnd);
      }

      SetFocus(engineChildHwnd);
    }

    private void StopEngineIfNeeded()
    {
      try
      {
        if (engineProcess != null && !engineProcess.HasExited)
        {
          // Closing this dock should destroy the child window inside it.
          // Also request close as a safeguard.
          engineProcess.CloseMainWindow();
          engineProcess.WaitForExit(500);

          if (!engineProcess.HasExited)
          {
            engineProcess.Kill();
          }
        }
      }
      catch
      {
        // Swallow cleanup errors on close
      }
      finally
      {
        engineProcess?.Dispose();
        engineProcess = null;
        engineChildHwnd = IntPtr.Zero;
      }
    }

    private class DoubleBufferedPanel : Panel
    {
      private IntPtr childHwnd = IntPtr.Zero;

      public void SetChildWindow(IntPtr hwnd)
      {
        childHwnd = hwnd;
      }

      protected override void WndProc(ref Message m)
      {
        const int WM_MOUSEACTIVATE = 0x21;
        const int MA_ACTIVATE = 1;

        // Mouse messages
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_RBUTTONUP = 0x0205;
        const int WM_MBUTTONDOWN = 0x0207;
        const int WM_MBUTTONUP = 0x0208;
        const int WM_MOUSEMOVE = 0x0200;
        const int WM_MOUSEWHEEL = 0x020A;

        // Keyboard messages
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_CHAR = 0x0102;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;

        if (m.Msg == WM_MOUSEACTIVATE)
        {
          m.Result = (IntPtr)MA_ACTIVATE;
          if (childHwnd != IntPtr.Zero)
          {
            SetFocus(childHwnd);
          }
          return;
        }

        // Forward input messages to child window
        if (childHwnd != IntPtr.Zero)
        {
          if ((m.Msg >= WM_MOUSEMOVE && m.Msg <= WM_MOUSEWHEEL) || (m.Msg >= WM_KEYDOWN && m.Msg <= WM_SYSKEYUP))
          {
            SendMessage(childHwnd, (uint)m.Msg, m.WParam, m.LParam);
            if (m.Msg >= WM_KEYDOWN && m.Msg <= WM_SYSKEYUP)
            {
              return; // Don't pass keyboard to base
            }
          }
        }

        base.WndProc(ref m);
      }

      public DoubleBufferedPanel()
      {
        DoubleBuffered = true;
        TabStop = true; // Allow this panel to receive focus
      }
    }

  } // class GameViewDeck

} // Namespace SwimEditor
