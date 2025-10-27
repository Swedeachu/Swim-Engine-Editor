using System.Drawing;
using System.Windows.Forms;

namespace SwimEditor
{

  public class DarkTabControl : TabControl
  {

    private readonly Color bg;          // page area background
    private readonly Color pageBg;      // unselected tab fill & page fill
    private readonly Color text;
    private readonly Color line;

    // extra shades to ensure contrast
    private readonly Color stripBg = Color.FromArgb(37, 37, 38); // VS header strip
    private readonly Color selTabBg = Color.FromArgb(63, 63, 70); // selected tab
    private readonly Color unselTabBg = Color.FromArgb(45, 45, 48); // unselected tab

    public DarkTabControl(Color bg, Color pageBg, Color text, Color line)
    {
      this.bg = bg;
      this.pageBg = pageBg;
      this.text = text;
      this.line = line;

      DrawMode = TabDrawMode.OwnerDrawFixed;
      Appearance = TabAppearance.Normal;
      Alignment = TabAlignment.Top;

      // visible header even at higher DPI
      SizeMode = TabSizeMode.Fixed;
      ItemSize = new Size(120, 28);

      Margin = new Padding(0);
      Padding = new Point(12, 6);

      SetStyle(ControlStyles.AllPaintingInWmPaint |
               ControlStyles.OptimizedDoubleBuffer |
               ControlStyles.UserPaint, true);

      BackColor = pageBg; // page area bg
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
      // fill everything in our colors so nothing white peeks through
      e.Graphics.Clear(pageBg);

      // header strip (area above DisplayRectangle)
      var strip = ClientRectangle;
      strip.Height = DisplayRectangle.Top;
      if (strip.Height > 0)
      {
        using (var b = new SolidBrush(stripBg))
          e.Graphics.FillRectangle(b, strip);
      }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      // paint page area explicitly
      var page = DisplayRectangle;
      using (var b = new SolidBrush(pageBg))
        e.Graphics.FillRectangle(b, page);

      // page border
      using (var p = new Pen(line))
      {
        var r = page; r.Width -= 1; r.Height -= 1;
        e.Graphics.DrawRectangle(p, r);
      }

      base.OnPaint(e); // triggers OnDrawItem for the tabs
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
      var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
      var rect = GetTabRect(e.Index); // do NOT deflate

      using (var back = new SolidBrush(selected ? selTabBg : unselTabBg))
      using (var border = new Pen(line))
      using (var fore = new SolidBrush(text))
      {
        e.Graphics.FillRectangle(back, rect);
        e.Graphics.DrawRectangle(border, rect);

        var caption = TabPages[e.Index].Text;
        TextRenderer.DrawText(
          e.Graphics, caption, Font, rect, text,
          TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis
        );
      }
    }

  }

}
