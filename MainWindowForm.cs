using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace SwimEditor
{

  public partial class MainWindowForm : Form
  {

    private DockPanel dockPanel;
    private VisualStudioToolStripExtender vsExtender;
    private ThemeBase theme;

    private MenuStrip mainMenu;
    private ToolStrip mainToolbar;

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

      theme = new VS2015DarkTheme(); // from WeifenLuo.WinFormsUI.Docking.Themes.VS2015

      // Create strips first so DockPanel.Fill will layout beneath them
      mainMenu = new MenuStrip();
      var fileMenu = new ToolStripMenuItem("File");
      fileMenu.DropDownItems.Add("New");
      fileMenu.DropDownItems.Add("Open...");
      fileMenu.DropDownItems.Add("Save");
      fileMenu.DropDownItems.Add(new ToolStripSeparator());
      fileMenu.DropDownItems.Add("Exit");
      mainMenu.Items.Add(fileMenu);

      mainToolbar = new ToolStrip();
      var playButton = new ToolStripButton("Play") { DisplayStyle = ToolStripItemDisplayStyle.Text };
      var stopButton = new ToolStripButton("Stop") { DisplayStyle = ToolStripItemDisplayStyle.Text };
      mainToolbar.Items.Add(playButton);
      mainToolbar.Items.Add(stopButton);

      // DockPanel (center workspace)
      dockPanel = new DockPanel
      {
        Dock = DockStyle.Fill,
        Theme = theme,
        DocumentStyle = DocumentStyle.DockingMdi
      };

      // Theme the strips 
      vsExtender = new VisualStudioToolStripExtender();
      vsExtender.SetStyle(mainMenu, VisualStudioToolStripExtender.VsVersion.Vs2015, theme);
      vsExtender.SetStyle(mainToolbar, VisualStudioToolStripExtender.VsVersion.Vs2015, theme);

      // Add controls in this order so menu/toolstrip sit at top, dock fills the rest
      Controls.Add(dockPanel);
      Controls.Add(mainToolbar);
      Controls.Add(mainMenu);

      MainMenuStrip = mainMenu;

      // Layout persistence path
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      layoutPath = System.IO.Path.Combine(appData, "SwimEditor", "layout.xml");
      System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(layoutPath));
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

      // Give the bottom more verticality *before* showing console
      dockPanel.DockBottomPortion = 300d; // or 0.35;
      console.Show(dockPanel, DockState.DockBottom);

      console.AppendLog("Swim Engine Editor v1.0");
      // for (int i = 0; i < 50; i++) { console.AppendLog("Output console test: " + i); }

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
  }

}
