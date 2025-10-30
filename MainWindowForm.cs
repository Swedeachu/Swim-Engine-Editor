using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using ReaLTaiizor.Controls;

namespace SwimEditor
{

  public partial class MainWindowForm : Form
  {

    private DockPanel dockPanel;
    private ThemeBase theme;

    // these 2 are unused
    private CrownMenuStrip mainMenu;
    private CrownToolStrip mainToolbar;

    private HierarchyDock hierarchy;
    private InspectorDock inspector;
    private GameViewDock gameView;
    private UtilityDock console;

    private string layoutPath;

    // transport buttons (stored as fields so we can update state/colors)
    private CrownButton playButton;
    private CrownButton pauseButton;
    private CrownButton stopButton;

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
          TabStop = false // prevents focus and thus avoids blue outline
        };

        // Disable any remaining focus/hover highlight
        btn.GotFocus += (s, e) => btn.TabStop = false;

        return btn;
      }

      playButton = MakeBtn("Play");
      pauseButton = MakeBtn("Pause");
      stopButton = MakeBtn("Stop");

      // subtle hover cue
      void AccentOn(object? s, EventArgs e) => ((CrownButton)s!).ForeColor = SwimEditorTheme.Accent;
      void AccentOff(object? s, EventArgs e) => ((CrownButton)s!).ForeColor = SwimEditorTheme.Text;
      foreach (var b in new[] { playButton, pauseButton, stopButton })
      {
        b.MouseEnter += AccentOn;
        b.MouseLeave += AccentOff;
      }

      // wire clicks
      playButton.Click += OnPlayClicked;
      pauseButton.Click += OnPauseClicked;
      stopButton.Click += OnStopClicked;

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
      // Make it so game view cout stream callback logs to the console
      gameView.EngineConsoleLine += line => console.AppendLog(line);

      // keep inspector synced
      hierarchy.OnSelectionChanged += obj => inspector.SetInspectedObject(obj);

      // Initial toolbar: assume the GameView will auto-start the engine on Shown.
      // We want Play disabled, Pause/Stop enabled right away.
      playButton.Enabled = false;
      pauseButton.Enabled = true;
      stopButton.Enabled = true;
      SetActive(playButton, false);
      SetActive(pauseButton, false);
      SetActive(stopButton, false);

      // When the engine actually starts/stops/pauses, keep the toolbar in sync.
      gameView.EngineStateChanged += () =>
      {
        // marshal UI update
        if (InvokeRequired) BeginInvoke(new Action(UpdateTransportUi));
        else UpdateTransportUi();
      };

      // Only load layout if it exists; it will override sizes
      LoadLayoutIfExists();

      // Final pass (in case layout changes visibility/creation timing)
      // Schedule after idle to catch GameView’s Shown/start.
      BeginInvoke(new Action(UpdateTransportUi));
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

    // --- Transport logic wiring ---

    private void OnPlayClicked(object? sender, EventArgs e)
    {
      // safety: play does nothing if already active (running and not paused)
      if (gameView != null && !(gameView.IsEngineRunning && !gameView.IsEnginePaused))
      {
        gameView.PlayEngine(); // starts or resumes as needed
        UpdateTransportUi();
      }
    }

    private void OnPauseClicked(object? sender, EventArgs e)
    {
      if (gameView == null || !gameView.IsEngineRunning) return;

      if (!gameView.IsEnginePaused)
      {
        gameView.PauseEngine();
        pauseButton.Text = "Resume";
      }
      else
      {
        gameView.ResumeEngine();
        pauseButton.Text = "Pause";
      }

      UpdateTransportUi();
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
      if (gameView == null || !gameView.IsEngineRunning) return;

      gameView.StopEngine();
      UpdateTransportUi();
    }

    private void UpdateTransportUi()
    {
      if (gameView == null)
      {
        SetBtnState(active: false, play: true, pause: false, stop: false);
        pauseButton.Text = "Pause";
        return;
      }

      bool running = gameView.IsEngineRunning;
      bool paused = gameView.IsEnginePaused;

      // Enablement rules
      playButton.Enabled = !running || paused; // can play if not running or paused
      pauseButton.Enabled = running;            // only when running
      stopButton.Enabled = running;            // only when running

      // Label the pause button based on state
      pauseButton.Text = paused ? "Resume" : "Pause";

      // Visual state (simple color UX)
      SetActive(playButton, running && !paused);
      SetActive(pauseButton, running && paused);
      SetActive(stopButton, false);
    }

    private void SetBtnState(bool active, bool play, bool pause, bool stop)
    {
      playButton.Enabled = play;
      pauseButton.Enabled = pause;
      stopButton.Enabled = stop;

      SetActive(playButton, active);
      SetActive(pauseButton, false);
      SetActive(stopButton, false);
    }

    private void SetActive(CrownButton btn, bool active)
    {
      // Accent background for active, keep others on Bg
      btn.BackColor = active ? SwimEditorTheme.Accent : SwimEditorTheme.Bg;
      btn.ForeColor = active ? Color.Black : SwimEditorTheme.Text;
    }

  } // class MainWindowForm

} // Namespace SwimEditor
