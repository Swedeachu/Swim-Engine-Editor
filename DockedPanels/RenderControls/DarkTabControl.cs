using SwimEditor;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace SwimEditor
{

  public class DarkTabControl : TabControl
  {

    private const int WM_ERASEBKGND = 0x0014;

    public DarkTabControl()
    {
      DrawMode = TabDrawMode.OwnerDrawFixed;
      Appearance = TabAppearance.Normal;
      Alignment = TabAlignment.Top;

      SizeMode = TabSizeMode.Fixed;
      ItemSize = new Size(120, 28);

      Margin = new Padding(0);
      Padding = new Point(12, 6);

      SetStyle(ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer |
               ControlStyles.UserPaint, true);

      BackColor = SwimEditorTheme.PageBg;

      ApplyThemeToPages();
    }

    protected override void OnCreateControl()
    {
      base.OnCreateControl();
      ApplyThemeToPages();
      ReflowForDpi();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
      base.OnControlAdded(e);
      if (e.Control is TabPage)
        ApplyThemeToPages();
    }

    protected override void OnFontChanged(EventArgs e)
    {
      base.OnFontChanged(e);
      ReflowForDpi();
    }

    // Inflate the page area so it covers the default 1px page border
    public override Rectangle DisplayRectangle
    {
      get
      {
        var r = base.DisplayRectangle;
        r.Inflate(2, 2);
        return r;
      }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
      // fill chrome around the content area to avoid any white showing through
      var g = e.Graphics;
      var cr = ClientRectangle;
      var dr = DisplayRectangle;

      if (dr.Top > 0) using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, new Rectangle(cr.Left, cr.Top, cr.Width, dr.Top));

      if (dr.Left > cr.Left) using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, new Rectangle(cr.Left, dr.Top, dr.Left - cr.Left, dr.Height));

      if (dr.Right < cr.Right) using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, new Rectangle(dr.Right, dr.Top, cr.Right - dr.Right, dr.Height));

      if (dr.Bottom < cr.Bottom) using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, new Rectangle(cr.Left, dr.Bottom, cr.Width, cr.Bottom - dr.Bottom));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      var g = e.Graphics;

      // fill entire control bg to ensure no white shows through
      using (var bg = new SolidBrush(SwimEditorTheme.PageBg))
        g.FillRectangle(bg, ClientRectangle);

      // draw tabs ourselves (selected and unselected)
      for (int i = 0; i < TabCount; i++)
      {
        Rectangle tabRect = GetTabRect(i);
        bool selected = (i == SelectedIndex);

        // slightly inset to avoid any GDI off-by-one artifacts
        var r = Rectangle.Inflate(tabRect, -2, -2);

        using (var back = new SolidBrush(selected ? SwimEditorTheme.Bg : SwimEditorTheme.PageBg))
        using (var border = new Pen(SwimEditorTheme.Line))
        {
          g.FillRectangle(back, r);
          g.DrawRectangle(border, r);
        }

        TextRenderer.DrawText(
            g,
            TabPages[i].Text,
            Font,
            r,
            SwimEditorTheme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
        );
      }
    }

    protected override void WndProc(ref Message m)
    {
      // keep blocking the default white erase
      if (m.Msg == WM_ERASEBKGND)
      {
        using (var g = Graphics.FromHdc(m.WParam))
        using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, ClientRectangle);
        m.Result = (IntPtr)1;
        return;
      }
      base.WndProc(ref m);
    }

    private void ApplyThemeToPages()
    {
      foreach (TabPage p in TabPages)
      {
        p.UseVisualStyleBackColor = false;
        p.BackColor = SwimEditorTheme.PageBg;
        p.ForeColor = SwimEditorTheme.Text;
        p.Padding = new Padding(0);
      }
    }

    private void ReflowForDpi()
    {
      // compute a stable tab height from font metrics + padding
      int textH = TextRenderer.MeasureText("Ag", Font).Height;
      int vertPad = 10; // was 6, slight bump for DPI; still compact
      int targetH = Math.Max(24, textH + vertPad);

      ItemSize = new Size(ItemSize.Width, targetH);
    }

  } // class DarkTabControl

} // Namespace SwimEditor