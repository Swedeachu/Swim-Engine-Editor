using WeifenLuo.WinFormsUI.Docking;
using TaiizorPanel = ReaLTaiizor.Controls.Panel;

namespace SwimEditor
{

  public class InspectorDock : DockContent
  {
    private PropertyGrid propertyGrid;
    private TaiizorPanel container;

    public InspectorDock()
    {
      // Container panel to provide consistent background and padding 
      container = new TaiizorPanel
      {
        Dock = DockStyle.Fill,
        BackColor = SwimEditorTheme.Panel
      };

      propertyGrid = new DarkPropertyGrid
      {
        Dock = DockStyle.Fill,
        ToolbarVisible = true,
        HelpVisible = true
      };

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
      if (obj == null)
      {
        propertyGrid.SelectedObject = null;
        return;
      }

      // Component -> custom inspector depending on type/name
      if (obj is SceneComponent comp)
      {
        var name = comp.Name ?? string.Empty;
        var nameLower = name.ToLowerInvariant();

        if (nameLower == "transform")
        {
          // Show parent as "name" or blank if none.
          propertyGrid.SelectedObject = TransformInspectorModel.FromJson(
            comp.RawJson,
            comp.OwnerParentId,
            comp.OwnerParentName
          );
          return;
        }

        if (nameLower == "material")
        {
          propertyGrid.SelectedObject = MaterialInspectorModel.FromJson(comp.RawJson);
          return;
        }

        // Fallback for any other component: generic JSON viewer (read-only)
        propertyGrid.SelectedObject = new JsonObjectView(name, comp.RawJson);
        return;
      }

      // Entity -> use a view model so we can show name properties and stuff nicely
      if (obj is SceneEntity ent)
      {
        propertyGrid.SelectedObject = EntityInspectorModel.FromEntity(ent);
        return;
      }

      // Fallback
      propertyGrid.SelectedObject = obj;
    }

  } // class InspectorDock

} // Namespace SwimEditor
