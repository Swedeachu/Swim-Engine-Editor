using System;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  // TODO: swim engine initialized onto this panel's surface
  public class GameViewDock : DockContent
  {

    private Panel renderSurface;

    public GameViewDock()
    {
      renderSurface = new DoubleBufferedPanel
      {
        Dock = DockStyle.Fill,
        BackColor = Color.Black
      };

      Controls.Add(renderSurface);
      ShowHint = DockState.Document;
    }

    public IntPtr RenderHandle => renderSurface.Handle;

    private class DoubleBufferedPanel : Panel
    {
      public DoubleBufferedPanel()
      {
        DoubleBuffered = true;
      }
    }

  }

}
