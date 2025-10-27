namespace SwimEditor
{
  partial class MainWindowForm
  {
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
      if (disposing && (components != null))
        components.Dispose();
      base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
      this.components = new System.ComponentModel.Container();
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.ClientSize = new System.Drawing.Size(1280, 800);
      this.Text = "Swim Engine Editor v1.0";
      this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
    }
  }

}
