using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  public class InspectorDock : DockContent
  {

    private PropertyGrid propertyGrid;

    public InspectorDock()
    {
      propertyGrid = new PropertyGrid
      {
        Dock = DockStyle.Fill,
        ToolbarVisible = true,
        HelpVisible = true
      };

      // Dark theme for PropertyGrid (needs explicit colors)
      var bg = Color.FromArgb(45, 45, 48);
      var viewBg = Color.FromArgb(30, 30, 30);
      var text = Color.Gainsboro;
      var line = Color.FromArgb(62, 62, 66);

      propertyGrid.BackColor = bg;
      propertyGrid.ForeColor = text;
      propertyGrid.ViewBackColor = viewBg;
      propertyGrid.ViewForeColor = text;
      propertyGrid.LineColor = line;

      propertyGrid.CategoryForeColor = text;
      propertyGrid.CategorySplitterColor = line;

      propertyGrid.HelpBackColor = viewBg;
      propertyGrid.HelpForeColor = text;

      propertyGrid.CommandsBackColor = bg;
      propertyGrid.CommandsForeColor = text;

      Controls.Add(propertyGrid);
      BackColor = bg;
    }

    public void SetInspectedObject(object obj)
    {
      propertyGrid.SelectedObject = obj;
    }

  } // class InspectorDock

} // Namespace SwimEditor
