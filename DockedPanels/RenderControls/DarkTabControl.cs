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

      // visible header even at higher DPI
      SizeMode = TabSizeMode.Fixed;
      ItemSize = new Size(120, 28);

      Margin = new Padding(0);
      Padding = new Point(12, 6);

      SetStyle(ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer, true);

      BackColor = SwimEditorTheme.PageBg;

      ApplyThemeToPages();
    }

    protected override void OnCreateControl()
    {
      base.OnCreateControl();
      ApplyThemeToPages();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
      base.OnControlAdded(e);
      if (e.Control is TabPage)
        ApplyThemeToPages();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
      // paint chrome around the page area in our theme color
      var g = e.Graphics;
      var cr = ClientRectangle;
      var dr = DisplayRectangle;

      // top header strip
      if (dr.Top > 0)
      {
        var top = new Rectangle(cr.Left, cr.Top, cr.Width, dr.Top);
        using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, top);
      }

      // left gutter
      if (dr.Left > cr.Left)
      {
        var left = new Rectangle(cr.Left, dr.Top, dr.Left - cr.Left, dr.Height);
        using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, left);
      }

      // right gutter
      if (dr.Right < cr.Right)
      {
        var right = new Rectangle(dr.Right, dr.Top, cr.Right - dr.Right, dr.Height);
        using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, right);
      }

      // bottom gutter
      if (dr.Bottom < cr.Bottom)
      {
        var bottom = new Rectangle(cr.Left, dr.Bottom, cr.Width, cr.Bottom - dr.Bottom);
        using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, bottom);
      }
    }

    protected override void WndProc(ref Message m)
    {
      // prevent base from erasing with default (white) and erase in our color instead
      if (m.Msg == WM_ERASEBKGND)
      {
        using (var g = Graphics.FromHdc(m.WParam))
        using (var b = new SolidBrush(SwimEditorTheme.PageBg))
          g.FillRectangle(b, ClientRectangle);
        m.Result = (IntPtr)1; // handled
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

  } // class DarkTabControl

} // Namespace SwimEditor
