namespace SwimEditor
{
  public class DarkPropertyGrid : PropertyGrid
  {
    private const int WM_ERASEBKGND = 0x0014;

    public DarkPropertyGrid()
    {
      // Behavior
      ToolbarVisible = false;        
      HelpVisible = false;           
      PropertySort = PropertySort.Categorized;

      // Flatten border
      ViewBorderColor = SwimEditorTheme.Line;
      HelpBorderColor = SwimEditorTheme.Line;

      // Base colors
      BackColor = SwimEditorTheme.Panel; // outer chrome
      ViewBackColor = SwimEditorTheme.Bg;    // grid cells
      ViewForeColor = SwimEditorTheme.Text;
      LineColor = SwimEditorTheme.Line;

      CategoryForeColor = SwimEditorTheme.Text;
      CategorySplitterColor = SwimEditorTheme.Line;

      HelpBackColor = SwimEditorTheme.Bg;
      HelpForeColor = SwimEditorTheme.Text;

      CommandsBackColor = SwimEditorTheme.Panel;
      CommandsForeColor = SwimEditorTheme.Text;

      // Flicker reduction
      SetStyle(ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer |
               ControlStyles.UserPaint, true);

      HandleCreated += (_, __) =>
      {
        StripChrome();
        ThemeChildrenRecursive(this);
      };

      ControlAdded += (_, e) =>
      {
        StripChrome();
        ThemeChildrenRecursive(e.Control);
      };
    }

    protected override void OnCreateControl()
    {
      base.OnCreateControl();
      StripChrome();
      ThemeChildrenRecursive(this);
    }

    protected override void OnFontChanged(EventArgs e)
    {
      base.OnFontChanged(e);
      ThemeChildrenRecursive(this);
    }

    protected override void WndProc(ref Message m)
    {
      if (m.Msg == WM_ERASEBKGND)
      {
        using (var g = Graphics.FromHdc(m.WParam))
        using (var b = new SolidBrush(SwimEditorTheme.Panel))
        {
          g.FillRectangle(b, ClientRectangle);
        }
        m.Result = (IntPtr)1;
        return;
      }

      base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
      using (var b = new SolidBrush(SwimEditorTheme.Panel))
      {
        e.Graphics.FillRectangle(b, ClientRectangle);
      }

      // Draw a simple 1px border like Crown panels
      using (var p = new Pen(SwimEditorTheme.Line))
      {
        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        e.Graphics.DrawRectangle(p, r);
      }
    }

    private void StripChrome()
    {
      // Make sure .NET doesn't recreate toolbar later
      ToolbarVisible = false;

      // Hide any ToolStrip that snuck in
      var toolbar = Controls.OfType<ToolStrip>().FirstOrDefault();
      if (toolbar != null)
      {
        toolbar.Visible = false;
        toolbar.Height = 0;
        toolbar.Margin = Padding.Empty;
      }

      // Remove any padding gaps the grid adds between sections
      Padding = Padding.Empty;
      Margin = Padding.Empty;
    }

    private void ThemeChildrenRecursive(Control c)
    {
      if (c == null) return;

      ThemeOne(c);

      foreach (Control child in c.Controls)
      {
        ThemeChildrenRecursive(child);
      }
    }

    private void ThemeOne(Control c)
    {
      string typeName = c.GetType().Name;

      // main grid area
      if (typeName == "PropertyGridView")
      {
        c.BackColor = SwimEditorTheme.Bg;
        c.ForeColor = SwimEditorTheme.Text;

        // remove odd borders / padding
        if (c is Control gridCtrl)
        {
          gridCtrl.Margin = Padding.Empty;
          gridCtrl.Padding = new Padding(1, 0, 1, 0);
        }

        // most of the weird white outlines are from default ControlBackColor /
        // window text; force theme colors
        c.ForeColor = SwimEditorTheme.Text;
      }
      // bottom help panel
      else if (typeName == "DocComment")
      {
        c.BackColor = SwimEditorTheme.Bg;
        c.ForeColor = SwimEditorTheme.Text;
        c.Margin = Padding.Empty;
        c.Padding = new Padding(4, 2, 4, 2);
      }
      // any ToolStrip that survived: hide it
      else if (c is ToolStrip ts)
      {
        ts.Visible = false;
        ts.Height = 0;
      }
      // text-ish controls
      else if (c is Label || c is LinkLabel)
      {
        c.BackColor = SwimEditorTheme.Bg;
        c.ForeColor = SwimEditorTheme.Text;
      }
      else if (c is TextBox || c is ComboBox)
      {
        c.BackColor = SwimEditorTheme.InputBg;
        c.ForeColor = SwimEditorTheme.Text;

        if (c is ComboBox cb)
        {
          cb.FlatStyle = FlatStyle.Flat;
          cb.DropDownStyle = ComboBoxStyle.DropDownList;
        }
      }
      else if (c is ListBox lb)
      {
        lb.BackColor = SwimEditorTheme.Bg;
        lb.ForeColor = SwimEditorTheme.Text;
        lb.BorderStyle = BorderStyle.None;
      }
      else
      {
        // Generic fallback: push toward Crown colors, but don't stomp transparent
        if (c.BackColor == SystemColors.Window ||
            c.BackColor == SystemColors.Control)
        {
          c.BackColor = SwimEditorTheme.Bg;
        }

        if (c.ForeColor == SystemColors.WindowText ||
            c.ForeColor == SystemColors.ControlText)
        {
          c.ForeColor = SwimEditorTheme.Text;
        }
      }
    }

  } // class DarkPropertyGrid

} // namespace SwimEditor
