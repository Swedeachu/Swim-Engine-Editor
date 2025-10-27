using System;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  public class HierarchyDock : DockContent
  {
    private TreeView treeView;

    public event Action<object> OnSelectionChanged;

    public HierarchyDock()
    {
      // PersistString = GetType().FullName;

      // Dark theme needs explicit colors
      treeView = new TreeView
      {
        Dock = DockStyle.Fill,
        HideSelection = false,
        BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(45, 45, 48),
        ForeColor = Color.Gainsboro
      };

      treeView.AfterSelect += (s, e) =>
      {
        OnSelectionChanged?.Invoke(e.Node);
      };

      // Example scene hierarchy
      var root = treeView.Nodes.Add("Scene");
      root.Nodes.Add("Main Camera");
      root.Nodes.Add("Directional Light");
      var obj = root.Nodes.Add("GameObject");
      obj.Nodes.Add("Transform");
      obj.Nodes.Add("MeshRenderer");
      root.Expand();

      Controls.Add(treeView);
    }
  }

}
