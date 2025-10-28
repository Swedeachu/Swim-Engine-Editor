using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwimEditor
{

  /// <summary>
  /// Console-like log with a dark, owner-painted scrollbar (no white OS track).
  /// Top panel shows the log, bottom panel is a simple input bar.
  /// </summary>
  public class ConsoleLogControl : UserControl
  {

    [DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_GETLINECOUNT = 0x00BA;
    private const int EM_LINESCROLL = 0x00B6;

    private readonly SplitContainer layout;
    private readonly Panel logHost;
    private readonly RichTextBox log;
    private readonly DarkScrollBar vbar;
    private readonly TextBox input;

    public event Action<string> CommandEntered;

    public ConsoleLogControl()
    {
      BackColor = SwimEditorTheme.PageBg;

      // Layout: top = log host (custom scrollbar), bottom = input
      layout = new SplitContainer();
      layout.Dock = DockStyle.Fill;
      layout.Orientation = Orientation.Horizontal;
      layout.FixedPanel = FixedPanel.Panel2;
      layout.IsSplitterFixed = true;
      layout.SplitterWidth = 1;
      layout.Panel2MinSize = 20;
      layout.Height = Height;

      // Host for log + custom scrollbar (we pad on the right by scrollbar width)
      logHost = new Panel();
      logHost.Dock = DockStyle.Fill;
      logHost.Margin = Padding.Empty;
      logHost.Padding = Padding.Empty;
      logHost.BackColor = SwimEditorTheme.PageBg;

      // Log (read-only, selectable, caret hidden)
      log = new RichTextBox();
      log.Dock = DockStyle.Fill;
      log.ReadOnly = true;
      log.BorderStyle = BorderStyle.None;
      log.BackColor = SwimEditorTheme.PageBg;
      log.ForeColor = SwimEditorTheme.Text;
      log.ScrollBars = RichTextBoxScrollBars.None; // hide native (white) scrollbar
      log.WordWrap = false;
      log.TabStop = false;
      log.Cursor = Cursors.Arrow;

      // Custom dark scrollbar
      vbar = new DarkScrollBar();
      vbar.Width = 12;
      vbar.Dock = DockStyle.Right;
      vbar.Margin = Padding.Empty;
      vbar.SetScrollHooks(log, logHost, SyncVBarFromControl);
      vbar.HookAutoHideLayout(logHost);

      logHost.Padding = new Padding(0, 0, vbar.Width, 0);

      // Sync RichTextBox <-> custom scrollbar
      log.VScroll += delegate { SyncVBarFromControl(); };
      log.TextChanged += delegate { SyncVBarFromControl(); };
      log.Resize += delegate { SyncVBarFromControl(); };
      vbar.ScrollValueChanged += delegate (int newValue) { ScrollLogTo(newValue); };

      // Hide caret on focus interactions
      log.GotFocus += delegate { HideCaret(log.Handle); };
      log.Enter += delegate { HideCaret(log.Handle); };
      log.MouseDown += delegate { HideCaret(log.Handle); };
      log.MouseUp += delegate { HideCaret(log.Handle); };
      log.SelectionChanged += delegate { HideCaret(log.Handle); };
      log.MouseWheel += delegate { SyncVBarFromControl(); };

      logHost.Controls.Add(log);
      logHost.Controls.Add(vbar);
      layout.Panel1.Controls.Add(logHost);

      // Input (single-line, caret visible)
      input = new TextBox();
      input.BorderStyle = BorderStyle.FixedSingle;
      input.BackColor = SwimEditorTheme.PageBg;
      input.ForeColor = SwimEditorTheme.Text;
      input.AutoSize = false;
      input.Margin = new Padding(0);
      input.Height = input.PreferredHeight;

      input.KeyDown += (s, e) =>
      {
        if (e.KeyCode == Keys.Enter)
        {
          e.SuppressKeyPress = true;
          string command = input.Text;
          if (!string.IsNullOrWhiteSpace(command))
          {
            AppendLine("> " + command);
            if (CommandEntered != null) CommandEntered(command);
            input.Clear();
          }
        }
      };

      // Input bar (prompt + textbox)
      var inputBar = new Panel();
      inputBar.Dock = DockStyle.Fill;
      inputBar.Margin = new Padding(0);
      inputBar.Padding = new Padding(0);
      inputBar.BackColor = SwimEditorTheme.PageBg;

      var prompt = new Label();
      prompt.Text = ">";
      prompt.AutoSize = false;
      prompt.Margin = new Padding(0);
      prompt.Padding = new Padding(0);
      prompt.ForeColor = SwimEditorTheme.Text;
      prompt.TextAlign = ContentAlignment.MiddleLeft;

      inputBar.Controls.Add(prompt);
      inputBar.Controls.Add(input);

      // Local layout for pixel-perfect alignment
      void LayoutInputBar()
      {
        int h = input.PreferredHeight;
        int leftPad = 4;
        int gap = 6;
        prompt.SetBounds(leftPad, 0, TextRenderer.MeasureText(prompt.Text, prompt.Font).Width, h);
        int x = prompt.Right + gap;
        int w = Math.Max(10, inputBar.ClientSize.Width - x - 4);
        input.SetBounds(x, 0, w, h);
        layout.Panel2MinSize = h;
      }

      inputBar.Resize += (s, e) => LayoutInputBar();
      LayoutInputBar();

      layout.Panel2.Controls.Add(inputBar);
      Controls.Add(layout);

      SyncVBarFromControl();
    }

    private void SyncVBarFromControl()
    {
      int totalLines = Math.Max(1, (int)SendMessage(log.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero));
      int first = (int)SendMessage(log.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
      int bottomChar = log.GetCharIndexFromPosition(new Point(1, Math.Max(1, log.ClientSize.Height - 1)));
      int bottomLine = log.GetLineFromCharIndex(bottomChar);
      int visible = Math.Max(1, bottomLine - first + 1);

      vbar.SetRange(0, Math.Max(0, totalLines - 1), visible, 1);
      int clamped = Math.Max(0, Math.Min(first, vbar.Maximum - vbar.LargeChange + 1));
      vbar.Value = clamped;
    }

    private void ScrollLogTo(int targetFirstVisibleLine)
    {
      int currentFirst = (int)SendMessage(log.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
      int delta = targetFirstVisibleLine - currentFirst;
      if (delta != 0)
      {
        SendMessage(log.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
        HideCaret(log.Handle);
      }
    }

    public void AppendLine(string text)
    {
      if (log.TextLength > 0) log.AppendText(Environment.NewLine);
      log.AppendText(text);
      log.SelectionStart = log.TextLength;
      log.SelectionLength = 0;
      log.ScrollToCaret();
      SyncVBarFromControl();
      HideCaret(log.Handle);
    }

    public void Clear()
    {
      log.Clear();
      SyncVBarFromControl();
    }

    public void FocusInput()
    {
      input.Focus();
      input.Select(input.TextLength, 0);
    }

    public string LogText
    {
      get { return log.Text; }
    }

  } // class ConsoleLogControl

} // Namespace SwimEditor
