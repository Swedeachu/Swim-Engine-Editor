using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using ReaLTaiizor.Controls;

namespace SwimEditor
{

  public partial class MainWindowForm : Form
  {

    private DockPanel dockPanel;
    private ThemeBase theme;

    private CrownMenuStrip mainMenu;
    private CrownToolStrip mainToolbar;

    private HierarchyDock hierarchy;
    private InspectorDock inspector;
    private GameViewDock gameView;
    private UtilityDock console;

    private string layoutPath;

    public MainWindowForm()
    {
      InitializeComponent();
      InitializeDockingUi();
      CreateAndShowPanes();
    }

    // TODO: top bar for like file, settings, etc in top left corner
    private void InitializeDockingUi()
    {
      Text = "Swim Engine Editor v1.0";
      WindowState = FormWindowState.Maximized;
      IsMdiContainer = true;

      theme = new VS2015DarkTheme();

      // Non-selectable host bar (no Crown header/blue line)
      var topBar = new System.Windows.Forms.Panel
      {
        Dock = DockStyle.Top,
        Height = 44,
        BackColor = SwimEditorTheme.Bg,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        TabStop = false
      };

      // Centered row of buttons
      var centerRow = new System.Windows.Forms.FlowLayoutPanel
      {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        BackColor = Color.Transparent
      };

      CrownButton MakeBtn(string text)
      {
        var btn = new CrownButton
        {
          Text = text,
          AutoSize = false,
          Size = new Size(96, 28),
          Margin = new Padding(8, 8, 8, 8),
          Padding = new Padding(10, 2, 10, 2),
          BackColor = SwimEditorTheme.Bg,
          ForeColor = SwimEditorTheme.Text,
          TabStop = false  // prevents focus and thus avoids blue outline
        };

        // Disable any remaining focus/hover highlight
        btn.GotFocus += (s, e) => btn.TabStop = false;

        return btn;
      }

      var playButton = MakeBtn("Play");
      var pauseButton = MakeBtn("Pause");
      var stopButton = MakeBtn("Stop");

      // subtle hover cue
      void AccentOn(object? s, EventArgs e) => ((CrownButton)s!).ForeColor = SwimEditorTheme.Accent;
      void AccentOff(object? s, EventArgs e) => ((CrownButton)s!).ForeColor = SwimEditorTheme.Text;
      foreach (var b in new[] { playButton, pauseButton, stopButton })
      {
        b.MouseEnter += AccentOn;
        b.MouseLeave += AccentOff;
      }

      centerRow.Controls.Add(playButton);
      centerRow.Controls.Add(pauseButton);
      centerRow.Controls.Add(stopButton);

      // center the row within the bar
      void LayoutCenter()
      {
        var pref = centerRow.PreferredSize;
        int x = Math.Max(0, (topBar.ClientSize.Width - pref.Width) / 2);
        int y = Math.Max(0, (topBar.ClientSize.Height - pref.Height) / 2);
        centerRow.Location = new Point(x, y);
      }
      topBar.Controls.Add(centerRow);
      topBar.Resize += (s, e) => LayoutCenter();
      LayoutCenter();

      // DockPanel workspace
      dockPanel = new DockPanel
      {
        Dock = DockStyle.Fill,
        Theme = theme,
        DocumentStyle = DocumentStyle.DockingMdi,
        BackColor = SwimEditorTheme.Bg
      };

      Controls.Add(dockPanel);
      Controls.Add(topBar);

      string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      layoutPath = Path.Combine(appData, "SwimEditor", "layout.xml");
      Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
    }

    // TODO: serialize each panels width and height on last program close and their position and dock state
    private void CreateAndShowPanes()
    {
      hierarchy = new HierarchyDock { Text = "Hierarchy" };
      inspector = new InspectorDock { Text = "Inspector" };
      gameView = new GameViewDock { Text = "Game View" };
      console = new UtilityDock { Text = "Utility" };

      gameView.Show(dockPanel, DockState.Document);
      hierarchy.Show(dockPanel, DockState.DockLeft);
      inspector.Show(dockPanel, DockState.DockRight);

      // Give the bottom more verticality before showing console
      dockPanel.DockBottomPortion = 300d; // or 0.35;
      console.Show(dockPanel, DockState.DockBottom);

      console.AppendLog("Swim Engine Editor v1.0");

      hierarchy.OnSelectionChanged += obj => inspector.SetInspectedObject(obj);

      // Only load layout if it exists; it will override sizes
      LoadLayoutIfExists();
    }

    private void LoadLayoutIfExists()
    {
      if (File.Exists(layoutPath))
      {
        try
        {
          dockPanel.LoadFromXml(layoutPath, DeserializeDockContent);
        }
        catch
        {
          // Ignore layout errors
        }
      }
    }

    private IDockContent DeserializeDockContent(string persistString)
    {
      if (persistString == typeof(HierarchyDock).FullName)
        return hierarchy;

      if (persistString == typeof(InspectorDock).FullName)
        return inspector;

      if (persistString == typeof(GameViewDock).FullName)
        return gameView;

      if (persistString == typeof(UtilityDock).FullName)
        return console;

      return null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      base.OnFormClosing(e);
      try
      {
        dockPanel.SaveAsXml(layoutPath);
      }
      catch
      {
        // Ignore save errors
      }
    }

  } // class MainWindowForm

} // Namespace SwimEditor
