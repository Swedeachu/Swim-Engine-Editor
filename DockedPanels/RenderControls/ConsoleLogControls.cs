using System.Windows.Forms;
using System.Drawing;

namespace SwimEditor
{

  public class ConsoleLogControl : UserControl
  {

    private readonly TextBox box;

    public ConsoleLogControl()
    {
      BackColor = SwimEditorTheme.PageBg;

      box = new TextBox
      {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        BorderStyle = BorderStyle.None
      };

      Controls.Add(box);
    }

    public void AppendLine(string text) => box.AppendText(text + "\r\n");
    public void Clear() => box.Clear();

  }

}
