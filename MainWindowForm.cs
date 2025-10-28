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

    private void InitializeDockingUi()
    {
      Text = "Swim Engine Editor v1.0";
      WindowState = FormWindowState.Maximized;
      IsMdiContainer = true;

      theme = new VS2015DarkTheme(); 

      // TODO centered and with icons like green flag, red square, etc, and a pause button somehow
      mainToolbar = new CrownToolStrip
      {
        Dock = DockStyle.Top,
        GripStyle = ToolStripGripStyle.Hidden,
        BackColor = SwimEditorTheme.Bg,
        ForeColor = SwimEditorTheme.Text,
        Padding = new Padding(6, 2, 6, 2)
      };

      var playButton = new ToolStripButton("Play")
      {
        DisplayStyle = ToolStripItemDisplayStyle.Text,
        ForeColor = SwimEditorTheme.Text
      };

      var stopButton = new ToolStripButton("Stop")
      {
        DisplayStyle = ToolStripItemDisplayStyle.Text,
        ForeColor = SwimEditorTheme.Text
      };

      // Optional simple accent cues on hover/click (Crown respects ToolStrip rendermode/colors)
      playButton.MouseEnter += (s, e) => playButton.ForeColor = SwimEditorTheme.Accent;
      playButton.MouseLeave += (s, e) => playButton.ForeColor = SwimEditorTheme.Text;
      stopButton.MouseEnter += (s, e) => stopButton.ForeColor = SwimEditorTheme.Accent;
      stopButton.MouseLeave += (s, e) => stopButton.ForeColor = SwimEditorTheme.Text;

      mainToolbar.Items.Add(playButton);
      mainToolbar.Items.Add(stopButton);

      // DockPanel (center workspace)
      dockPanel = new DockPanel
      {
        Dock = DockStyle.Fill,
        Theme = theme,
        DocumentStyle = DocumentStyle.DockingMdi,
        BackColor = SwimEditorTheme.Bg
      };

      // Add controls in this order so menu/toolstrip sit at top, dock fills the rest
      Controls.Add(dockPanel);
      Controls.Add(mainToolbar);

      MainMenuStrip = mainMenu;

      // Layout persistence path
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      layoutPath = Path.Combine(appData, "SwimEditor", "layout.xml");
      Directory.CreateDirectory(Path.GetDirectoryName(layoutPath));
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
