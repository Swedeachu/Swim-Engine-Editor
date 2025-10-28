using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  /// <summary>
  /// A multipurpose dock with tabs (Debug Log, File View, etc.)
  /// </summary>
  public class UtilityDock : DockContent
  {
    private readonly TabControl tabs;
    private readonly ConsoleLogControl log;
    private readonly FileViewControl fileView;

    public UtilityDock()
    {
      BackColor = SwimEditorTheme.Bg;
      Padding = new Padding(0); // avoid any host edge seam

      tabs = new DarkTabControl
      {
        Dock = DockStyle.Fill,
        DrawMode = TabDrawMode.OwnerDrawFixed,
        SizeMode = TabSizeMode.Fixed,
        ItemSize = new Size(60, 20),
        Padding = new Point(20, 6),
        HotTrack = true,
        BackColor = SwimEditorTheme.Bg,
        Margin = Padding.Empty
      };

      // Debug Log
      log = new ConsoleLogControl();
      var logTab = new TabPage("Console")
      {
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        UseVisualStyleBackColor = false,
        Padding = new Padding(0)
      };

      log.Dock = DockStyle.Fill;
      logTab.Controls.Add(log);
      tabs.TabPages.Add(logTab);

      // File View
      fileView = new FileViewControl();
      var fileTab = new TabPage("File View")
      {
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        UseVisualStyleBackColor = false,
        Padding = new Padding(0)
      };

      fileView.Dock = DockStyle.Fill;
      fileTab.Controls.Add(fileView);
      tabs.TabPages.Add(fileTab);

      Controls.Add(tabs);
    }

    public void AppendLog(string text) => log.AppendLine(text);
    public void ClearLog() => log.Clear();

    public void SetFileRoot(string path) => fileView.SetRoot(path);
    public void NavigateFileView(string path) => fileView.NavigateTo(path);

  } // class UtilityDock

} // Namespace SwimEditor
