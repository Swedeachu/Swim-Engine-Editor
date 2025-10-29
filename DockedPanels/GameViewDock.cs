using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  public class GameViewDock : DockContent
  {

    private Panel renderSurface;
    private Process engineProcess;

    public GameViewDock()
    {
      renderSurface = new DoubleBufferedPanel
      {
        Dock = DockStyle.Fill,
        BackColor = Color.Black
      };

      Controls.Add(renderSurface);
      ShowHint = DockState.Document;

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
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Failed to launch Swim Engine:\n{ex.Message}", "Swim Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }

    private void StopEngineIfNeeded()
    {
      try
      {
        if (engineProcess != null && !engineProcess.HasExited)
        {
          // Since the engine created a child window owned by the editor panel, closing this dock
          // should tear down that child window which will drive engine shutdown.
          // As a safeguard, also request process close if still running.
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
      }
    }

    private class DoubleBufferedPanel : Panel
    {
      public DoubleBufferedPanel()
      {
        DoubleBuffered = true;
      }
    }

  } // class GameViewDeck

} // Namespace SwimEditor
