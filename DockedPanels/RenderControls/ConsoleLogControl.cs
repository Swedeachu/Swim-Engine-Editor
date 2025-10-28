using System.Runtime.InteropServices;
using System.ComponentModel;
using ReaLTaiizor.Controls;
using ReaLTaiizor.Enum.Poison;

namespace SwimEditor
{

  /// <summary>
  /// Console-like log using ReaLTaiizor PoisonTextBox (flat, no white outline) + CrownScrollBar.
  /// - Custom dark scrollbar (CrownScrollBar) stays in sync with the log
  /// - Mouse wheel over the text scrolls (we drive EM_LINESCROLL), bar syncs
  /// - Autoscroll only if already at bottom
  /// - Bounded line count (trimming)
  /// - Command history (Up/Down)
  /// - Hides caret in the log
  /// </summary>
  public class ConsoleLogControl : UserControl
  {

    [DllImport("user32.dll")] private static extern bool HideCaret(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_GETLINECOUNT = 0x00BA;
    private const int EM_LINESCROLL = 0x00B6;
    private const int WM_SETREDRAW = 0x000B;

    private readonly SplitContainer layout;

    private readonly System.Windows.Forms.Panel logHost;     // log + custom scrollbar
    private readonly CrownScrollBar vbar;                    // custom dark scrollbar
    private readonly FlatPoisonTextBox log;                  // multiline, flat (no border draw)
    private readonly FlatPoisonTextBox input;                // single-line, flat

    private readonly System.Windows.Forms.Panel inputBar;    // holds separator + prompt + textbox
    private readonly System.Windows.Forms.Panel inputSep;    // 1px divider

    private readonly System.Collections.Generic.List<string> history = new System.Collections.Generic.List<string>();
    private int historyIndex = -1;

    private int _maxLines = 5000;

    public event Action<string> CommandEntered;

    public ConsoleLogControl()
    {
      BackColor = SwimEditorTheme.PageBg;

      // Layout: top = log host (log + custom vbar), bottom = input
      layout = new SplitContainer
      {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        FixedPanel = FixedPanel.Panel2,
        IsSplitterFixed = true,
        SplitterWidth = 1,
        Panel2MinSize = 20,
        Height = Height
      };

      // Host panel for log + custom scrollbar; rely on pure docking (no manual padding)
      logHost = new System.Windows.Forms.Panel
      {
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        BackColor = SwimEditorTheme.PageBg
      };

      // Multiline flat PoisonTextBox (no white outline), no native scrollbars
      log = new FlatPoisonTextBox
      {
        Dock = DockStyle.Fill,
        Theme = ThemeStyle.Dark,
        Style = ColorStyle.Blue,

        UseCustomBackColor = true,
        UseCustomForeColor = true,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,

        Multiline = true,
        ReadOnly = true,
        ShortcutsEnabled = true,
        ScrollBars = ScrollBars.None,   // hide native bar; use CrownScrollBar
        TabStop = false,
        ShowButton = false,
        ShowClearButton = false
      };

      // Configure inner TextBox (wheel, caret hiding, no word-wrap, colors)
      log.HandleCreated += (s, e) =>
      {
        var inner = GetInnerTextBox(log);
        if (inner != null)
        {
          inner.WordWrap = false;
          inner.Cursor = Cursors.Arrow;

          inner.BackColor = SwimEditorTheme.PageBg;
          inner.ForeColor = SwimEditorTheme.Text;

          void hide() { if (inner.IsHandleCreated) HideCaret(inner.Handle); }
          inner.GotFocus += (s2, e2) => hide();
          inner.Enter += (s2, e2) => hide();
          inner.MouseDown += (s2, e2) => hide();
          inner.MouseUp += (s2, e2) => hide();
          inner.KeyUp += (s2, e2) => hide();

          // manual wheel => LINESCROLL (so scrolling works even without native bar)
          inner.MouseWheel += (s2, e2) =>
          {
            int linesPerNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            int notches = e2.Delta / 120;
            int scrollLines = -notches * linesPerNotch;
            if (scrollLines != 0)
            {
              SendMessage(inner.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)scrollLines);
              hide();
              SyncBarFromInner();
            }
          };

          // sync on content/size changes
          inner.TextChanged += (s2, e2) => SyncBarFromInner();
          inner.Resize += (s2, e2) => SyncBarFromInner();
        }

        // First sync once created
        SyncBarFromInner();
      };

      // Keep layout perfect when entering/leaving (e.g., tabbing back in)
      log.Enter += (s, e) => SyncBarFromInner();
      log.Leave += (s, e) => SyncBarFromInner();

      // Optional: on hover, focus inner so wheel works instantly
      log.MouseEnter += (s, e) =>
      {
        var inner = GetInnerTextBox(log);
        if (inner != null) inner.Focus();
      };

      // Custom dark scrollbar (Crown) — docked to RIGHT, no manual padding
      vbar = new CrownScrollBar
      {
        Dock = DockStyle.Right,
        Width = 16,                   // align with ThemeProvider sizes (>= ScrollBarSize)
        Margin = Padding.Empty
      };
      vbar.ScrollOrientation = ReaLTaiizor.Enum.Crown.ScrollOrientation.Vertical;
      vbar.ValueChanged += (s, e) => ScrollLogTo(e.Value);

      // Add in dock-order: add vbar first, then log (so log fills remaining space cleanly)
      logHost.Controls.Add(vbar);
      logHost.Controls.Add(log);

      layout.Panel1.Controls.Add(logHost);

      // INPUT (single-line, flat)
      input = new FlatPoisonTextBox
      {
        Theme = ThemeStyle.Dark,
        Style = ColorStyle.Blue,

        UseCustomBackColor = true,
        UseCustomForeColor = true,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,

        Multiline = false,
        ShortcutsEnabled = true,
        ShowButton = false,
        ShowClearButton = false,
        Margin = new Padding(0)
      };

      input.HandleCreated += (s, e) =>
      {
        var inner = GetInnerTextBox(input);
        if (inner != null)
        {
          inner.BackColor = SwimEditorTheme.PageBg;
          inner.ForeColor = SwimEditorTheme.Text;
        }
      };

      inputBar = new System.Windows.Forms.Panel
      {
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Padding(0),
        BackColor = SwimEditorTheme.PageBg
      };

      inputSep = new System.Windows.Forms.Panel
      {
        Dock = DockStyle.Top,
        Height = 1,
        BackColor = SwimEditorTheme.Line
      };

      var prompt = new System.Windows.Forms.Label
      {
        Text = ">",
        AutoSize = false,
        Margin = new Padding(0),
        Padding = new Padding(0),
        ForeColor = SwimEditorTheme.Text,
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = SwimEditorTheme.PageBg
      };

      inputBar.Controls.Add(prompt);
      inputBar.Controls.Add(input);
      inputBar.Controls.Add(inputSep);

      // Pixel-perfect layout for input row
      void LayoutInputBar()
      {
        var inner = GetInnerTextBox(input);
        int h = (inner != null) ? inner.PreferredHeight + 6 : input.PreferredSize.Height;
        int leftPad = 4;
        int gap = 6;
        int y = inputSep.Bottom;

        prompt.SetBounds(leftPad, y, TextRenderer.MeasureText(prompt.Text, prompt.Font).Width, h);
        int x = prompt.Right + gap;
        int w = Math.Max(10, inputBar.ClientSize.Width - x - 4);
        input.SetBounds(x, y, w, h);

        layout.Panel2MinSize = h + inputSep.Height;
      }

      inputBar.Resize += (s, e) => LayoutInputBar();
      input.HandleCreated += (s, e) => LayoutInputBar();

      layout.Panel2.Controls.Add(inputBar);
      Controls.Add(layout);

      // Input behavior
      input.KeyDown += InputKeyDown;

      // keep bar synced on control-level size changes too
      Resize += (s, e) => SyncBarFromInner();
      VisibleChanged += (s, e) => SyncBarFromInner();
    }

    // Access the inner TextBox Poison hosts
    private static TextBox GetInnerTextBox(PoisonTextBox host)
    {
      return (host != null && host.Controls.Count > 0) ? host.Controls[0] as TextBox : null;
    }

    // Sync custom scrollbar from the inner TextBox state
    private void SyncBarFromInner()
    {
      var inner = GetInnerTextBox(log);
      if (inner == null || !inner.IsHandleCreated) return;

      int totalLines = Math.Max(1, (int)SendMessage(inner.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero));
      int first = (int)SendMessage(inner.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

      int bottomChar = inner.GetCharIndexFromPosition(new Point(1, Math.Max(1, inner.ClientSize.Height - 1)));
      int bottomLine = inner.GetLineFromCharIndex(bottomChar);
      int visible = Math.Max(1, bottomLine - first + 1);

      vbar.Minimum = 0;
      vbar.Maximum = totalLines;
      vbar.ViewSize = visible;

      int maxFirst = Math.Max(0, vbar.Maximum - vbar.ViewSize);
      int clampedFirst = Math.Max(0, Math.Min(first, maxFirst));
      if (vbar.Value != clampedFirst)
        vbar.Value = clampedFirst;

      // Only toggle visibility; docking handles layout (no manual padding)
      vbar.Visible = vbar.Maximum > vbar.ViewSize;
    }

    // Scroll inner TextBox to a target first visible line (diff with current)
    private void ScrollLogTo(int targetFirstVisibleLine)
    {
      var inner = GetInnerTextBox(log);
      if (inner == null || !inner.IsHandleCreated) return;

      int currentFirst = (int)SendMessage(inner.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
      int delta = targetFirstVisibleLine - currentFirst;
      if (delta != 0)
      {
        SendMessage(inner.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
        HideCaret(inner.Handle);
      }
    }

    private void InputKeyDown(object sender, KeyEventArgs e)
    {
      if (e.KeyCode == Keys.Enter)
      {
        e.SuppressKeyPress = true;

        string command = input.Text;
        if (!string.IsNullOrWhiteSpace(command))
        {
          AppendLine("> " + command);
          history.Add(command);
          historyIndex = history.Count;
          CommandEntered?.Invoke(command);
          input.Clear();
          ParseEngineCommand(command);
        }
      }
      else if (e.KeyCode == Keys.Up)
      {
        if (history.Count > 0)
        {
          historyIndex = Math.Max(0, historyIndex - 1);
          input.Text = history[historyIndex];
          var inner = GetInnerTextBox(input);
          if (inner != null)
          {
            inner.SelectionStart = inner.TextLength;
            inner.SelectionLength = 0;
          }
        }
        e.SuppressKeyPress = true;
      }
      else if (e.KeyCode == Keys.Down)
      {
        if (history.Count > 0)
        {
          historyIndex = Math.Min(history.Count, historyIndex + 1);
          input.Text = (historyIndex >= history.Count) ? "" : history[historyIndex];
          var inner = GetInnerTextBox(input);
          if (inner != null)
          {
            inner.SelectionStart = inner.TextLength;
            inner.SelectionLength = 0;
          }
        }
        e.SuppressKeyPress = true;
      }
      else if (e.Control && e.KeyCode == Keys.L)
      {
        Clear();
        e.SuppressKeyPress = true;
      }
    }

    private void ParseEngineCommand(string command)
    {
      if (command == null) return;

      if (command == "cls" || command == "clear")
      {
        Clear();
        return;
      }
    }

    /// <summary>
    /// Append a line. If already at bottom, keep autoscrolling; otherwise preserve scroll.
    /// Also trims old lines if the maximum line count is exceeded.
    /// </summary>
    public void AppendLine(string text)
    {
      if (IsDisposed) return;

      var inner = GetInnerTextBox(log);
      if (inner == null)
      {
        if (log.Text.Length > 0) log.AppendText(Environment.NewLine);
        log.AppendText(text);
        return;
      }

      if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLine), text); return; }

      bool isAtBottom = IsAtBottom(inner);

      BeginUpdate(inner);
      try
      {
        if (log.Text.Length > 0) log.AppendText(Environment.NewLine);
        log.AppendText(text);

        TrimLogIfNeeded(inner);

        if (isAtBottom)
        {
          inner.SelectionStart = inner.TextLength;
          inner.SelectionLength = 0;
          SnapToBottom(inner);
        }
      }
      finally
      {
        EndUpdate(inner);
        HideCaret(inner.Handle);
        SyncBarFromInner();
      }
    }

    /// <summary>Clears the log content.</summary>
    public void Clear()
    {
      var inner = GetInnerTextBox(log);
      if (inner == null) { log.Clear(); return; }

      if (InvokeRequired) { BeginInvoke(new Action(Clear)); return; }

      BeginUpdate(inner);
      try
      {
        log.Clear();
      }
      finally
      {
        EndUpdate(inner);
        SyncBarFromInner();
      }
    }

    /// <summary>Focuses the input box, placing caret at end.</summary>
    public void FocusInput()
    {
      var inner = GetInnerTextBox(input);
      if (inner != null)
      {
        inner.Focus();
        inner.SelectionStart = inner.TextLength;
        inner.SelectionLength = 0;
      }
      else
      {
        input.Focus();
      }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MaxLines
    {
      get { return _maxLines; }
      set { _maxLines = Math.Max(100, value); }
    }

    public string LogText => log.Text;

    // --- utilities ----------------------------------------------------------

    private static bool IsAtBottom(TextBox inner)
    {
      if (inner == null || !inner.IsHandleCreated) return true;

      int totalLines = Math.Max(1, (int)SendMessage(inner.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero));
      int first = (int)SendMessage(inner.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
      int bottomChar = inner.GetCharIndexFromPosition(new Point(1, Math.Max(1, inner.ClientSize.Height - 1)));
      int bottomLine = inner.GetLineFromCharIndex(bottomChar);
      int visible = Math.Max(1, bottomLine - first + 1);

      return (first + visible) >= totalLines;
    }

    private void TrimLogIfNeeded(TextBox inner)
    {
      if (inner == null || !inner.IsHandleCreated) return;

      int totalLines = (int)SendMessage(inner.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
      if (totalLines <= _maxLines) return;

      int removeLines = totalLines - _maxLines;
      if (removeLines <= 0) return;

      int start = 0;
      int end = inner.GetFirstCharIndexFromLine(removeLines);
      if (end > start)
      {
        int selStart = inner.SelectionStart;
        int selLen = inner.SelectionLength;

        inner.Select(start, end - start);
        inner.SelectedText = string.Empty;

        inner.SelectionStart = Math.Max(0, selStart - (end - start));
        inner.SelectionLength = selLen;
      }
    }

    private static void SnapToBottom(TextBox inner)
    {
      int totalLines = Math.Max(1, (int)SendMessage(inner.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero));
      int first = (int)SendMessage(inner.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
      int bottomChar = inner.GetCharIndexFromPosition(new Point(1, Math.Max(1, inner.ClientSize.Height - 1)));
      int bottomLine = inner.GetLineFromCharIndex(bottomChar);
      int visible = Math.Max(1, bottomLine - first + 1);

      int targetFirst = Math.Max(0, totalLines - visible);
      int delta = targetFirst - first;
      if (delta != 0)
      {
        SendMessage(inner.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
      }
    }

    private static void BeginUpdate(TextBox inner)
    {
      if (inner == null || !inner.IsHandleCreated) return;
      SendMessage(inner.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
    }

    private static void EndUpdate(TextBox inner)
    {
      if (inner == null || !inner.IsHandleCreated) return;
      SendMessage(inner.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
      inner.Invalidate();
    }

  } // class ConsoleLogControl


  /// <summary>
  /// Flat PoisonTextBox: removes PoisonTextBox border/outline and enforces our dark text/bg.
  /// </summary>
  internal class FlatPoisonTextBox : PoisonTextBox
  {
    protected override void OnPaintForeground(PaintEventArgs e)
    {
      // Ensure inner PromptedTextBox uses our colors (no white outlines/forecolor regressions)
      if (Controls.Count > 0 && Controls[0] is TextBox inner)
      {
        inner.BackColor = BackColor;
        inner.ForeColor = ForeColor;
      }

      // Intentionally skip drawing the default PoisonTextBox border/outline.
      // No base.OnPaintForeground(e) here to avoid the white rectangle.
    }
  }

} // Namespace SwimEditor
