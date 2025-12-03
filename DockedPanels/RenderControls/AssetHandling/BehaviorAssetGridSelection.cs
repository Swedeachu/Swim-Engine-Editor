using System.Drawing.Drawing2D;

namespace SwimEditor
{

  /// <summary>
  /// Popup that shows all registered behaviors from AssetDatabase.Behaviors
  /// in a custom grid layout, with themed drawing and a custom icon.
  /// Double-click or press OK to send the selected behavior to the engine.
  /// </summary>
  public class BehaviorAssetGridSelection : AssetGridSelection
  {
    private readonly int entityId;

    private static Image cachedBehaviorIcon;

    public BehaviorAssetGridSelection(int entityId)
      : base("Select Behavior")
    {
      this.entityId = entityId;

      RefreshItems();
    }

    protected override IEnumerable<string> GetAssetKeys()
    {
      return AssetDatabase.Behaviors;
    }

    protected override void OnAssetChosen(string assetKey)
    {
      SendBehaviorToEngine(assetKey);
    }

    protected override Image GetAssetIcon(string assetKey)
    {
      if (cachedBehaviorIcon == null)
      {
        cachedBehaviorIcon = CreateBehaviorIcon();
      }

      return cachedBehaviorIcon;
    }

    protected override string GetEmptyMessage()
    {
      return "No behaviors registered.";
    }

    private void SendBehaviorToEngine(string behaviorKey)
    {
      if (string.IsNullOrWhiteSpace(behaviorKey))
      {
        return;
      }

      string cmd = $"(scene.entity.addBehavior {entityId} \"{behaviorKey}\")";
      MainWindowForm.Instance.GameView.SendEngineMessage(cmd);

      DialogResult = DialogResult.OK;
      Close();
    }

    private static Image CreateBehaviorIcon()
    {
      int size = 32;
      var bmp = new Bitmap(size, size);

      using (var g = Graphics.FromImage(bmp))
      {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        Rectangle r = new Rectangle(1, 1, size - 2, size - 2);

        // Dark script panel background
        using (var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 60)))
        {
          g.FillRectangle(bgBrush, r);
        }

        // Soft border
        using (var borderPen = new Pen(Color.FromArgb(220, 220, 255), 2f))
        {
          g.DrawRectangle(borderPen, r);
        }

        // Bracket-like "script" glyph
        using (var accentPen = new Pen(Color.LimeGreen, 2f))
        {
          int midY = r.Top + r.Height / 2;
          int left = r.Left + 6;
          int right = r.Right - 6;

          // [
          g.DrawLine(accentPen, left, midY - 6, left, midY + 6);
          g.DrawLine(accentPen, left, midY - 6, left + 4, midY - 6);
          g.DrawLine(accentPen, left, midY + 6, left + 4, midY + 6);

          // ]
          g.DrawLine(accentPen, right, midY - 6, right, midY + 6);
          g.DrawLine(accentPen, right, midY - 6, right - 4, midY - 6);
          g.DrawLine(accentPen, right, midY + 6, right - 4, midY + 6);
        }
      }

      return bmp;
    }

  } // class BehaviorAssetGridSelection

} // namespace SwimEditor
