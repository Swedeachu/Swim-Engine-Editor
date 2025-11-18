using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WeifenLuo.WinFormsUI.Docking;
using Timer = System.Windows.Forms.Timer;
using System.ComponentModel;
using System.Drawing;

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

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SuspendThread(IntPtr hThread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr hThread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr processHandle);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr processHandle);
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    // Overload for process communication via WM_COPYDATA
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct THREADENTRY32
    {
      public uint dwSize;
      public uint cntUsage;
      public uint th32ThreadID;
      public uint th32OwnerProcessID;
      public int tpBasePri;
      public int tpDeltaPri;
      public uint dwFlags;
    }

    private const uint TH32CS_SNAPTHREAD = 0x00000004;
    private const uint THREAD_SUSPEND_RESUME = 0x0002;

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_QUIT = 0x0012;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    private const uint WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
      public IntPtr dwData;   // optional channel/tag
      public int cbData;      // size in bytes
      public IntPtr lpData;   // pointer to data
    }

    private Panel renderSurface;
    private Process engineProcess;
    private IntPtr engineChildHwnd = IntPtr.Zero;

    // Subscribe to receive each console line from the engine
    public event Action<string> EngineConsoleLine;
    public event Action<string> RawEngineMessage;

    // Notify editor to refresh transport UI when engine state changes
    public event Action EngineStateChanged;

    // Queue to hold messages until engineChildHwnd is ready
    private readonly Queue<string> pendingEngineMessages = new Queue<string>();

    private DataReceivedEventHandler outputHandler;
    private DataReceivedEventHandler errorHandler;
    private Timer childPollTimer;

    private NativeWindow parkingWindow; // hidden top-level to "park" the child during handle recreation
    private IntPtr parkingHwnd = IntPtr.Zero;

    private bool shuttingDown = false;

    // state property for pause
    public bool IsEngineRunning => engineProcess != null && !engineProcess.HasExited;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsEnginePaused { get; private set; } = false;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsEngineEditing { get; private set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsEngineStopped { get; private set; } = true;

    // P/Invoke for focus and child enumeration
    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    public GameViewDock()
    {
      renderSurface = new DoubleBufferedPanel
      {
        Dock = DockStyle.Fill,
        BackColor = Color.Black
      };

      // Hook IPC and pause provider immediately so messages can be received
      if (renderSurface is DoubleBufferedPanel panel)
      {
        panel.SetPausedProvider(() => IsEnginePaused);

        // ensure single subscription
        panel.EngineMessageReceived -= OnEngineMessageReceived;
        panel.EngineMessageReceived += OnEngineMessageReceived;
      }

      Controls.Add(renderSurface);
      ShowHint = DockState.Document;

      // keep the child filling the panel (important for hit-testing)
      renderSurface.Resize += (_, __) =>
      {
        if (engineChildHwnd != IntPtr.Zero && !IsEnginePaused)
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

    public void PlayEngine()
    {
      // If not running, start it
      if (!IsEngineRunning)
      {
        shuttingDown = false;
        StartEngineIfNeeded();
        IsEnginePaused = false;
        RaiseEngineStateChanged();
        return;
      }

      // If paused, resume
      if (IsEnginePaused)
      {
        ResumeEngine();
      }
      // else already running: do nothing (safety)
    }

    public void StopEngine()
    {
      if (!IsEngineRunning) return;

      shuttingDown = true;     // disable parking so child can be destroyed
      IsEnginePaused = false;  // reset our flag

      // Ask nicely to quit (WM_QUIT + WM_CLOSE)
      RequestEngineClose();

      // Do not block UI; cleanup on a small timer
      var t = new Timer { Interval = 200 };
      t.Tick += (s, e) =>
      {
        if (engineProcess == null || engineProcess.HasExited)
        {
          t.Stop();
          t.Dispose();
          StopEngineIfNeeded(); // final cleanup (disposes process, clears handles)
          RaiseEngineStateChanged();
        }
      };
      t.Start();
    }

    public void PauseEngine()
    {
      if (!IsEngineRunning || IsEnginePaused) return;

      try
      {
        // Important: disable the child HWND first so Windows won't hit-test it while suspended
        if (engineChildHwnd != IntPtr.Zero) EnableWindow(engineChildHwnd, false);

        // Suspend only the child process; editor stays responsive
        NtSuspendProcess(engineProcess.Handle);

        IsEnginePaused = true;
        RaiseEngineStateChanged();
      }
      catch
      {
        // If something failed, try to re-enable just in case
        if (engineChildHwnd != IntPtr.Zero) EnableWindow(engineChildHwnd, true);
        IsEnginePaused = false;
        RaiseEngineStateChanged();
      }
    }

    public void ResumeEngine()
    {
      if (!IsEngineRunning || !IsEnginePaused) return;

      try
      {
        // Resume the process first
        NtResumeProcess(engineProcess.Handle);

        // Then re-enable the child HWND (now safe to receive input again)
        if (engineChildHwnd != IntPtr.Zero) EnableWindow(engineChildHwnd, true);

        IsEnginePaused = false;
        RaiseEngineStateChanged();
      }
      catch
      {
        // ignore; keep UI responsive
      }
    }

    // Called when the play button is pressed, will unpause us and then start us in play mode as gaming for a clean play test run
    public void GoIntoPlayMode()
    {
      IsEngineStopped = false;
      IsEnginePaused = false;
      IsEngineEditing = false;
      SendEngineMessage("resume");
      SendEngineMessage("game"); // takes us out of edit mode
      SendEngineMessage("play");
    }

    // Called when stop button is pressed, takes us out of play mode into stopped which unpauses us and puts us back to editing
    public void GoIntoStoppedMode()
    {
      IsEngineStopped = true;
      IsEnginePaused = false;
      IsEngineEditing = true;
      SendEngineMessage("stop");
      SendEngineMessage("resume"); // not sure if we want to call this first or last for intended behavior during state changes
      SendEngineMessage("edit");
    }

    // Called when pause button is pressed, simply takes us into Paused mode, all other states remain
    public void GoIntoPauseMode()
    {
      SendEngineMessage("pause"); 
      IsEnginePaused = true;
    }

    // Called when pause button is pressed while things are paused, simply takes us out of Paused mode, all other states remain
    public void GoIntoResumedMode()
    {
      SendEngineMessage("resume"); // unpauses us
      IsEnginePaused = false;
    }

    // Called when edit button is pressed, simply takes us into edit mode, all other states 
    public void GoIntoEditMode()
    {
      SendEngineMessage("edit"); // takes us into edit mode for free cam and no player controller 
      IsEngineEditing = true;
    }

    // Called when edit button is pressed while things are in edit mode, simply takes us out of edit mode, all other states remain
    public void GoIntoGameMode()
    {
      SendEngineMessage("game"); // takes us out of edit mode
      IsEngineEditing = false;
    }

    // Variadic-style message sender.
    // Formats the message and sends via WM_COPYDATA as UTF-16 (wide) with a NUL terminator.
    public void SendEngineMessage(string message, params object[] args)
    {
      if (string.IsNullOrEmpty(message)) return;

      string payload = (args != null && args.Length > 0)
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args)
        : message;

      // If the child HWND isn't ready yet, queue it.
      if (engineChildHwnd == IntPtr.Zero)
      {
        pendingEngineMessages.Enqueue(payload);
        return;
      }

      // Try immediate send; if it fails (rare), re-queue once.
      if (!SendCopyDataString(payload))
      {
        pendingEngineMessages.Enqueue(payload);
      }
    }

    // Try to flush any queued messages when the child HWND is available.
    private void TryFlushPendingMessages()
    {
      if (engineChildHwnd == IntPtr.Zero) return;

      int safety = 256; // avoid infinite loops
      while (pendingEngineMessages.Count > 0 && safety-- > 0)
      {
        var s = pendingEngineMessages.Peek();
        if (SendCopyDataString(s))
        {
          pendingEngineMessages.Dequeue();
        }
        else
        {
          // Child not accepting right now; stop trying for this tick.
          break;
        }
      }
    }

    // Core WM_COPYDATA send (UTF-16, NUL-terminated)
    private bool SendCopyDataString(string s, ulong channel = 1)
    {
      if (engineChildHwnd == IntPtr.Zero) return false;

      // Marshal as UTF-16 with explicit NUL terminator
      IntPtr strPtr = IntPtr.Zero;
      try
      {
        // Include trailing NUL; StringToHGlobalUni already includes it.
        strPtr = Marshal.StringToHGlobalUni(s);
        int byteCount = (s.Length + 1) * 2; // UTF-16 bytes, including NUL

        var cds = new COPYDATASTRUCT
        {
          dwData = (IntPtr)unchecked((long)channel),
          cbData = byteCount,
          lpData = strPtr
        };

        // SendMessage (synchronous)
        SendMessage(engineChildHwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
        return true;
      }
      catch
      {
        return false;
      }
      finally
      {
        if (strPtr != IntPtr.Zero) Marshal.FreeHGlobal(strPtr);
      }
    }

    private void SuspendAllThreadsOf(Process p)
    {
      IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
      if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return;

      try
      {
        var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
        if (!Thread32First(snap, ref te)) return;

        do
        {
          if (te.th32OwnerProcessID == (uint)p.Id)
          {
            IntPtr hThread = OpenThread(THREAD_SUSPEND_RESUME, false, te.th32ThreadID);
            if (hThread != IntPtr.Zero)
            {
              try { SuspendThread(hThread); }
              finally { CloseHandle(hThread); }
            }
          }
        }
        while (Thread32Next(snap, ref te));
      }
      finally
      {
        CloseHandle(snap);
      }
    }

    private void ResumeAllThreadsOf(Process p)
    {
      IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
      if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return;

      try
      {
        var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
        if (!Thread32First(snap, ref te)) return;

        do
        {
          if (te.th32OwnerProcessID == (uint)p.Id)
          {
            IntPtr hThread = OpenThread(THREAD_SUSPEND_RESUME, false, te.th32ThreadID);
            if (hThread != IntPtr.Zero)
            {
              try { while (ResumeThread(hThread) > 0) { /* resume fully */ } }
              finally { CloseHandle(hThread); }
            }
          }
        }
        while (Thread32Next(snap, ref te));
      }
      finally
      {
        CloseHandle(snap);
      }
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
        Arguments = $"--parent-hwnd {RenderHandle} --state editing", // start the engine in an editing state
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

        // Keep UI in sync when process exits on its own
        engineProcess.Exited += (_, __) =>
        {
          try { BeginInvoke(new Action(() => { RaiseEngineStateChanged(); })); } catch { }
        };

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
            RaiseEngineStateChanged();
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

      // Just attach the child handle; IPC wiring is done in the constructor
      if (renderSurface is DoubleBufferedPanel panel)
      {
        panel.SetChildWindow(engineChildHwnd);
      }

      SetFocus(engineChildHwnd);

      SendEngineMessage("Attatching IPC to Engine from Editor");

      // flush any queued IPC
      TryFlushPendingMessages();
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
        pendingEngineMessages.Clear();

        outputHandler = null;
        errorHandler = null;

        engineProcess?.Dispose();
        engineProcess = null;
        engineChildHwnd = IntPtr.Zero;

        RaiseEngineStateChanged();
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

    private void RaiseEngineStateChanged()
    {
      try { EngineStateChanged?.Invoke(); } catch { }
    }

    private void OnEngineMessageReceived(ulong channel, string text)
    {
      EngineConsoleLine?.Invoke($"[Message from Engine to Editor: {channel}] {text}");
      RawEngineMessage?.Invoke(text);

      // This should be parsed elsewhere in MainWindowForm to be able to talk to the other panels such as the hierarchy 
    }

    private class DoubleBufferedPanel : Panel
    {
      private IntPtr childHwnd = IntPtr.Zero;

      private Func<bool> isPausedProvider;

      // Subscribe to when the engine talks to us
      public event Action<ulong, string> EngineMessageReceived; // channel, text

      public void SetPausedProvider(Func<bool> provider)
      {
        isPausedProvider = provider;
      }

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

        if (m.Msg == WM_COPYDATA)
        {
          try
          {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
            ulong channel = (ulong)(long)cds.dwData;

            string? text = null;
            if (cds.lpData != IntPtr.Zero && cds.cbData >= 2)
            {
              // data is UTF-16 including trailing NUL; PtrToStringUni will stop at NUL.
              text = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2);
              if (text != null && text.Length > 0 && text[^1] == '\0')
                text = text[..^1];
            }

            EngineMessageReceived?.Invoke(channel, text ?? string.Empty);

            // Return nonzero to indicate handled
            m.Result = (IntPtr)1;
            return;
          }
          catch
          {
            // Consider returning 0 to indicate not handled if something goes wrong.
            m.Result = IntPtr.Zero;
            return;
          }
        }

        bool paused = isPausedProvider != null && isPausedProvider();

        if (m.Msg == WM_MOUSEACTIVATE)
        {
          if (paused)
          {
            // Don't activate/steal focus toward the suspended child
            m.Result = (IntPtr)MA_ACTIVATE; // activate host so keyboard stays here
            return;
          }

          m.Result = (IntPtr)MA_ACTIVATE;
          if (childHwnd != IntPtr.Zero)
          {
            SetFocus(childHwnd);
          }
          return;
        }

        // Forward input messages to child window (only when not paused)
        if (!paused && childHwnd != IntPtr.Zero)
        {
          if ((m.Msg >= WM_MOUSEMOVE && m.Msg <= WM_MOUSEWHEEL) || (m.Msg >= WM_KEYDOWN && m.Msg <= WM_SYSKEYUP))
          {
            // Keep this synchronous when running for accurate behavior
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

    } // class DoubleBufferedPanel

  } // class GameViewDeck

} // Namespace SwimEditor
