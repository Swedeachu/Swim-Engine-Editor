namespace SwimEditor
{

  /// <summary>
  /// Popup that shows all registered materials from AssetDatabase.Materials
  /// in a custom grid layout, with themed drawing and a CrownScrollBar.
  /// Double-click or press OK to send the selected material to the engine.
  /// </summary>
  public class MaterialAssetGridSelection : AssetGridSelection
  {
    private readonly int entityId;

    private static Image cachedMaterialIcon;

    public MaterialAssetGridSelection(int entityId)
      : base("Select Material")
    {
      this.entityId = entityId;

      RefreshItems();
    }

    protected override IEnumerable<string> GetAssetKeys()
    {
      return AssetDatabase.Materials;
    }

    protected override void OnAssetChosen(string assetKey)
    {
      SendMaterialToEngine(assetKey);
    }

    protected override Image GetAssetIcon(string assetKey)
    {
      if (cachedMaterialIcon == null)
      {
        cachedMaterialIcon = CreateMaterialIcon();
      }

      return cachedMaterialIcon;
    }

    protected override string GetEmptyMessage()
    {
      return "No materials registered.";
    }

    private void SendMaterialToEngine(string materialKey)
    {
      if (string.IsNullOrWhiteSpace(materialKey))
      {
        return;
      }

      string cmd = $"(scene.entity.setMaterial {entityId} \"{materialKey}\")";
      MainWindowForm.Instance.GameView.SendEngineMessage(cmd);

      DialogResult = DialogResult.OK;
      Close();
    }

    private static Image CreateMaterialIcon()
    {
      int size = 32;
      var bmp = new Bitmap(size, size);

      using (var g = Graphics.FromImage(bmp))
      {
        g.Clear(Color.Transparent);

        Rectangle r = new Rectangle(1, 1, size - 2, size - 2);

        using (var lgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
          r,
          Color.SteelBlue,
          Color.MediumPurple,
          45f))
        {
          g.FillRectangle(lgBrush, r);
        }

        using (var p = new Pen(Color.White, 2f))
        {
          g.DrawRectangle(p, r);
          g.DrawLine(p, r.Left + 4, r.Bottom - 6, r.Right - 4, r.Bottom - 6);
          g.DrawLine(p, r.Left + 4, r.Top + 6, r.Right - 4, r.Top + 6);
        }
      }

      return bmp;
    }

  } // class MaterialAssetGridSelection

} // namespace SwimEditor
