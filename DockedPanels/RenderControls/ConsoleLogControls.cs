using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwimEditor
{

  public class ConsoleLogControl : UserControl
  {

    [DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    private readonly SplitContainer layout;
    private readonly RichTextBox log;
    private readonly TextBox input;

    public event Action<string> CommandEntered;

    public ConsoleLogControl()
    {
      BackColor = SwimEditorTheme.PageBg;

      // Layout: top = log, bottom = input
      layout = new SplitContainer();
      layout.Dock = DockStyle.Fill;
      layout.Orientation = Orientation.Horizontal;
      layout.FixedPanel = FixedPanel.Panel2;
      layout.IsSplitterFixed = true;
      layout.SplitterWidth = 1;
      layout.Panel2MinSize = 20; // input bar height (will be adjusted below)
      layout.Height = Height;

      // Log (read-only, selectable, caret hidden)
      log = new RichTextBox();
      log.Dock = DockStyle.Fill;
      log.ReadOnly = true;
      log.BorderStyle = BorderStyle.None;
      log.BackColor = SwimEditorTheme.PageBg;
      log.ForeColor = SwimEditorTheme.Text;
      log.ScrollBars = RichTextBoxScrollBars.Vertical;
      log.WordWrap = false;
      log.TabStop = false; // don't tab into it
      log.Cursor = Cursors.Arrow;

      // Hide caret on any possible focus/selection interaction
      log.GotFocus += delegate { HideCaret(log.Handle); };
      log.Enter += delegate { HideCaret(log.Handle); };
      log.MouseDown += delegate { HideCaret(log.Handle); };
      log.MouseUp += delegate { HideCaret(log.Handle); };
      log.SelectionChanged += delegate { HideCaret(log.Handle); };

      layout.Panel1.Controls.Add(log);

      // Input (single-line, caret visible)
      input = new TextBox();
      input.BorderStyle = BorderStyle.FixedSingle;
      input.BackColor = SwimEditorTheme.PageBg;
      input.ForeColor = SwimEditorTheme.Text;
      input.AutoSize = false; // we control exact height/position
      input.Margin = new Padding(0);
      input.Height = input.PreferredHeight;

      // For now this just echos to the console, TODO: run callbacks in the engine for commands later
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

      // Pixel-perfect input bar with ">" prompt and textbox aligned on the same Y
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
      prompt.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

      // Arrange controls with exact coordinates so top edges match
      inputBar.Controls.Add(prompt);
      inputBar.Controls.Add(input);

      // Local layout function for precise alignment
      void LayoutInputBar()
      {
        // height = textbox natural height
        int h = input.PreferredHeight;

        // prompt size and position
        int leftPad = 4;
        int gap = 6;
        prompt.SetBounds(leftPad, 0, TextRenderer.MeasureText(prompt.Text, prompt.Font).Width, h);

        // textbox position: same Y as prompt (0), directly to the right
        int x = prompt.Right + gap;
        int w = Math.Max(10, inputBar.ClientSize.Width - x - 4);
        input.SetBounds(x, 0, w, h);

        // ensure bottom panel is at least this tall
        layout.Panel2MinSize = h;
      }

      inputBar.Resize += (s, e) => LayoutInputBar();
      // initial layout
      LayoutInputBar();

      layout.Panel2.Controls.Add(inputBar);
      Controls.Add(layout);
    }

    public void AppendLine(string text)
    {
      if (log.TextLength > 0) log.AppendText(Environment.NewLine);
      log.AppendText(text);

      // Scroll to bottom
      log.SelectionStart = log.TextLength;
      log.SelectionLength = 0;
      log.ScrollToCaret();

      // Ensure caret stays hidden in the log
      HideCaret(log.Handle);
    }

    public void Clear()
    {
      log.Clear();
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
