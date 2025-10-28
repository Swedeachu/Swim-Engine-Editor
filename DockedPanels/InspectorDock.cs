using System;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  // THIS IS JUST A PLACE HOLDER, WILL BE CUSTOMIZED LATER FOR EDITING TRANSFORMS, MATERIALS,TAGS, OTHER SERIALIZED COMPONENTS
  public class InspectorDock : DockContent
  {

    private PropertyGrid propertyGrid;
    private Panel container;

    public InspectorDock()
    {
      // Container panel to provide consistent background and padding if needed
      container = new Panel
      {
        Dock = DockStyle.Fill,
        BackColor = SwimEditorTheme.Panel
      };

      propertyGrid = new PropertyGrid
      {
        Dock = DockStyle.Fill,
        ToolbarVisible = true,
        HelpVisible = true
      };

      // Unity-like dark theme for PropertyGrid (explicit colors remain necessary)
      var bg = SwimEditorTheme.Panel;
      var viewBg = SwimEditorTheme.Bg;
      var text = SwimEditorTheme.Text;
      var line = SwimEditorTheme.Line;

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

      container.Controls.Add(propertyGrid);
      Controls.Add(container);

      BackColor = SwimEditorTheme.PageBg;
    }

    public void SetInspectedObject(object obj)
    {
      propertyGrid.SelectedObject = obj;
    }

  } // class InspectorDock

} // Namespace SwimEditor
