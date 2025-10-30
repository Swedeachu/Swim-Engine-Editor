using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
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
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_QUIT = 0x0012;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    private Panel renderSurface;
    private Process engineProcess;
    private IntPtr engineChildHwnd = IntPtr.Zero;

    // Subscribe to receive each console line from the engine.
    public event Action<string> EngineConsoleLine;

    private DataReceivedEventHandler outputHandler;
    private DataReceivedEventHandler errorHandler;
    private Timer childPollTimer;

    private NativeWindow parkingWindow; // hidden top-level to "park" the child during handle recreation
    private IntPtr parkingHwnd = IntPtr.Zero;

    private bool shuttingDown = false;

    // P/Invoke for focus and child enumeration
    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

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

      // park/unpark across handle/parent changes
      renderSurface.HandleDestroyed += RenderSurfaceHandleDestroyed;
      renderSurface.HandleCreated += RenderSurfaceHandleCreated;
      renderSurface.ParentChanged += RenderSurfaceParentChanged;
      EnsureParkingWindow();

      Shown += (_, __) => StartEngineIfNeeded();
      FormClosing += OnGameViewDockClosing;
      FormClosed += (_, __) => StopEngineIfNeeded();
    }

    // Tiny hidden window we can parent the child to while the panel handle is being recreated
    private void EnsureParkingWindow()
    {
      if (parkingWindow != null && parkingHwnd != IntPtr.Zero) return;

      parkingWindow = new NativeWindow();
      var cp = new CreateParams
      {
        Caption = "SwimParking",
        X = -10000,
        Y = -10000,
        Width = 1,
        Height = 1,
        Style = unchecked((int)0x80000000) // WS_POPUP
      };
      parkingWindow.CreateHandle(cp);
      parkingHwnd = parkingWindow.Handle;
    }

    // Before the panel's handle is destroyed (e.g., undock/float), reparent the engine child to the parking HWND
    private void RenderSurfaceHandleDestroyed(object sender, EventArgs e)
    {
      // If we are genuinely closing, do NOT park; let the child be destroyed.
      if (shuttingDown) return;

      // If our child exists, park it so it doesn't get destroyed with the panel
      if (engineChildHwnd != IntPtr.Zero && parkingHwnd != IntPtr.Zero)
      {
        SetParent(engineChildHwnd, parkingHwnd);
      }
    }

    // After the panel's handle is recreated, reparent the engine child back and resize it
    private void RenderSurfaceHandleCreated(object sender, EventArgs e)
    {
      if (engineChildHwnd != IntPtr.Zero)
      {
        SetParent(engineChildHwnd, renderSurface.Handle);
        MoveWindow(engineChildHwnd, 0, 0, renderSurface.ClientSize.Width, renderSurface.ClientSize.Height, false);
        BringWindowToTop(engineChildHwnd);
        SetFocus(engineChildHwnd);
      }
    }

    private void RenderSurfaceParentChanged(object sender, EventArgs e)
    {
      // If we are genuinely closing, do NOT park/unpark
      if (shuttingDown) return;

      // If being detached from a parent temporarily, park the child.
      if (engineChildHwnd == IntPtr.Zero || parkingHwnd == IntPtr.Zero) return;

      var parent = renderSurface.Parent;
      if (parent == null)
      {
        // moving between containers; keep child alive
        SetParent(engineChildHwnd, parkingHwnd);
      }
      else if (renderSurface.IsHandleCreated)
      {
        // back under a container; restore parenting
        SetParent(engineChildHwnd, renderSurface.Handle);
        MoveWindow(engineChildHwnd, 0, 0, renderSurface.ClientSize.Width, renderSurface.ClientSize.Height, false);
      }
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
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        CreateNoWindow = true
      };

      try
      {
        engineProcess = Process.Start(psi);
        engineProcess.EnableRaisingEvents = true;

        // hook stdout/stderr to EngineConsoleLine (via RaiseEngineConsole)
        outputHandler = (s, ea) =>
        {
          if (ea.Data != null) RaiseEngineConsole(ea.Data);
        };
        errorHandler = (s, ea) =>
        {
          if (ea.Data != null) RaiseEngineConsole("[err] " + ea.Data);
        };

        engineProcess.OutputDataReceived += outputHandler;
        engineProcess.ErrorDataReceived += errorHandler;
        engineProcess.BeginOutputReadLine();
        engineProcess.BeginErrorReadLine();

        // Poll for the child window (created by the engine with class name "SwimEngine")
        childPollTimer?.Stop();
        childPollTimer?.Dispose();
        childPollTimer = new Timer { Interval = 50 };

        int tries = 0;
        childPollTimer.Tick += (s, e) =>
        {
          tries++;
          CaptureEngineChildHandle();
          if (engineChildHwnd != IntPtr.Zero || tries > 120) // ~6s max
          {
            childPollTimer.Stop();
            AfterChildDiscovered();
          }
        };
        childPollTimer.Start();
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

    private void RequestEngineClose()
    {
      if (engineChildHwnd != IntPtr.Zero)
      {
        // Get the engine UI thread and post WM_QUIT so its PeekMessage loop exits cleanly
        uint pid;
        uint tid = GetWindowThreadProcessId(engineChildHwnd, out pid);
        if (tid != 0)
        {
          // Post WM_QUIT (no wParam/lParam needed). This does not require the HWND.
          PostThreadMessage(tid, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        // As a best-effort nudge, also send/ post WM_CLOSE (child may ignore, but harmless)
        IntPtr result;
        SendMessageTimeout(engineChildHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 200, out result);
        PostMessage(engineChildHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
      }
      else if (engineProcess != null && !engineProcess.HasExited)
      {
        // No child HWND; let StopEngineIfNeeded handle fallback kill if needed.
      }
    }

    private void OnGameViewDockClosing(object sender, FormClosingEventArgs e)
    {
      shuttingDown = true; // prevents parking during real close

      // Ask the engine to shut down (thread-quit + best-effort WM_CLOSE)
      RequestEngineClose();

      // Give it a brief moment to exit cleanly
      if (engineProcess != null && !engineProcess.HasExited)
      {
        try { engineProcess.WaitForExit(800); } catch { }
      }
    }

    private void StopEngineIfNeeded()
    {
      try
      {
        // stop & dispose polling timer
        if (childPollTimer != null)
        {
          childPollTimer.Stop();
          childPollTimer.Dispose();
          childPollTimer = null;
        }

        if (engineProcess != null)
        {
          // unhook stdout/stderr first to avoid callbacks during teardown
          if (outputHandler != null) engineProcess.OutputDataReceived -= outputHandler;
          if (errorHandler != null) engineProcess.ErrorDataReceived -= errorHandler;

          try { engineProcess.CancelOutputRead(); } catch { /* ignore */ }
          try { engineProcess.CancelErrorRead(); } catch { /* ignore */ }

          if (!engineProcess.HasExited)
          {
            // We already requested quit; give it one more short chance before kill
            try { engineProcess.WaitForExit(700); } catch { }

            if (!engineProcess.HasExited)
            {
              engineProcess.Kill();
              try { engineProcess.WaitForExit(700); } catch { }
            }
          }
        }
      }
      catch
      {
        // Swallow cleanup errors on close
      }
      finally
      {
        outputHandler = null;
        errorHandler = null;

        engineProcess?.Dispose();
        engineProcess = null;
        engineChildHwnd = IntPtr.Zero;
      }
    }

    // marshal to UI thread and raise the event
    private void RaiseEngineConsole(string line)
    {
      if (string.IsNullOrEmpty(line)) return;

      if (InvokeRequired)
      {
        BeginInvoke(new Action<string>(RaiseEngineConsole), line);
        return;
      }

      EngineConsoleLine?.Invoke(line);
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
