using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using System;

namespace SwimEditor
{

  /// <summary>
  /// A multipurpose dock with tabs (Debug Log, File View, etc.)
  /// </summary>
  public class UtilityDock : DockContent
  {

    private readonly TabControl _tabs;
    private readonly ConsoleLogControl _log;
    private readonly FileViewControl _fileView;

    public UtilityDock()
    {
      BackColor = SwimEditorTheme.Bg;

      _tabs = new TabControl
      {
        Dock = DockStyle.Fill,
        DrawMode = TabDrawMode.OwnerDrawFixed,

        SizeMode = TabSizeMode.Fixed,   // fixed height (and width, but we handle text with ellipsis)
        ItemSize = new Size(80, 20),   // width hint; height = 30px
        Padding = new Point(20, 6),     // inner padding of each tab (more readable)
        HotTrack = true
      };

      // reduce flicker on owner-draw
      _tabs.GetType().GetProperty("DoubleBuffered",
          System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
          ?.SetValue(_tabs, true, null);

      _tabs.DrawItem += (s, e) =>
      {
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var tabRect = e.Bounds;
        tabRect.Inflate(-2, -2);

        using (var back = new SolidBrush(selected ? SwimEditorTheme.Bg : SwimEditorTheme.PageBg))
        using (var border = new Pen(SwimEditorTheme.Line))
        {
          e.Graphics.FillRectangle(back, tabRect);
          e.Graphics.DrawRectangle(border, tabRect);
          var tabText = _tabs.TabPages[e.Index].Text;
          TextRenderer.DrawText(
            e.Graphics, tabText, _tabs.Font, tabRect, SwimEditorTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
          );
        }
      };

      // Debug Log
      _log = new ConsoleLogControl();
      var logTab = new TabPage("Debug Log")
      {
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        UseVisualStyleBackColor = false,
        Padding = new Padding(0) 
      };
      _log.Dock = DockStyle.Fill;     // ensure fill (redundant if already set inside control)
      logTab.Controls.Add(_log);
      _tabs.TabPages.Add(logTab);

      // File View
      _fileView = new FileViewControl();
      var fileTab = new TabPage("File View")
      {
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        UseVisualStyleBackColor = true,
        Padding = new Padding(0) 
      };
      _fileView.Dock = DockStyle.Fill;
      fileTab.Controls.Add(_fileView);
      _tabs.TabPages.Add(fileTab);

      Controls.Add(_tabs);
    }

    // Public API surface
    public void AppendLog(string text) => _log.AppendLine(text);
    public void ClearLog() => _log.Clear();

    public void SetFileRoot(string path) => _fileView.SetRoot(path);
    public void NavigateFileView(string path) => _fileView.NavigateTo(path);

  }

}
