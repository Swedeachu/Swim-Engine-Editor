using ReaLTaiizor.Controls;
using ReaLTaiizor.Util;
using System.ComponentModel;

namespace SwimEditor
{

  /// <summary>
  /// Popup that shows all registered materials from AssetDatabase.Materials
  /// in a custom grid layout, with themed drawing and a CrownScrollBar.
  /// Double-click or press OK to send the selected material to the engine.
  /// </summary>
  public class MaterialAssetGridSelection : Form
  {
    private readonly int entityId;

    private readonly MaterialGridControl grid;
    private readonly CrownScrollBar vBar;

    private readonly System.Windows.Forms.Panel mainPanel;
    private readonly System.Windows.Forms.Panel bottomPanel;

    private readonly System.Windows.Forms.Button okButton;
    private readonly System.Windows.Forms.Button cancelButton;

    public MaterialAssetGridSelection(int entityId)
    {
      this.entityId = entityId;

      Text = "Select Material";
      FormBorderStyle = FormBorderStyle.SizableToolWindow;
      StartPosition = FormStartPosition.CenterParent;
      MinimizeBox = false;
      MaximizeBox = false;
      ShowInTaskbar = false;

      BackColor = SwimEditorTheme.PageBg;
      ForeColor = SwimEditorTheme.Text;

      Font = new Font("Segoe UI", 9f, FontStyle.Regular);

      ClientSize = new Size(880, 420);

      // Main panel hosts grid + scrollbar
      mainPanel = new System.Windows.Forms.Panel
      {
        Dock = DockStyle.Fill,
        BackColor = SwimEditorTheme.PageBg
      };

      grid = new MaterialGridControl
      {
        Dock = DockStyle.Fill,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        MouseWheelScrollMultiplier = 48
      };

      vBar = new CrownScrollBar
      {
        Dock = DockStyle.Right,
        Width = 16,
        ScrollOrientation = ReaLTaiizor.Enum.Crown.ScrollOrientation.Vertical,
      };

      grid.AttachScrollBar(vBar);
      grid.SetMaterials(AssetDatabase.Materials);

      grid.ItemActivated += (s, e) =>
      {
        if (!string.IsNullOrWhiteSpace(e.MaterialKey))
        {
          SendMaterialToEngine(e.MaterialKey);
        }
      };

      mainPanel.Controls.Add(grid);
      mainPanel.Controls.Add(vBar);

      // Bottom panel with OK / Cancel buttons
      bottomPanel = new System.Windows.Forms.Panel
      {
        Dock = DockStyle.Bottom,
        Height = 40,
        Padding = new Padding(8, 4, 8, 4),
        BackColor = SwimEditorTheme.PageBg
      };

      okButton = new System.Windows.Forms.Button
      {
        Text = "OK",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(10, 4, 10, 4),

        FlatStyle = FlatStyle.Flat,
        TabStop = true,
        UseVisualStyleBackColor = false,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        Anchor = AnchorStyles.Right | AnchorStyles.Top
      };
      okButton.FlatAppearance.BorderSize = 1;
      okButton.FlatAppearance.BorderColor = SwimEditorTheme.HoverColor;
      okButton.FlatAppearance.MouseOverBackColor = SwimEditorTheme.HoverColor;
      okButton.FlatAppearance.MouseDownBackColor = SwimEditorTheme.HoverColor;

      okButton.Click += (s, e) =>
      {
        var key = grid.SelectedMaterialKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
          SendMaterialToEngine(key);
        }
      };

      cancelButton = new System.Windows.Forms.Button
      {
        Text = "Cancel",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(10, 4, 10, 4),

        FlatStyle = FlatStyle.Flat,
        TabStop = true,
        UseVisualStyleBackColor = false,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        Anchor = AnchorStyles.Right | AnchorStyles.Top
      };
      cancelButton.FlatAppearance.BorderSize = 1;
      cancelButton.FlatAppearance.BorderColor = SwimEditorTheme.HoverColor;
      cancelButton.FlatAppearance.MouseOverBackColor = SwimEditorTheme.HoverColor;
      cancelButton.FlatAppearance.MouseDownBackColor = SwimEditorTheme.HoverColor;

      cancelButton.Click += (s, e) =>
      {
        DialogResult = DialogResult.Cancel;
        Close();
      };

      bottomPanel.Controls.Add(cancelButton);
      bottomPanel.Controls.Add(okButton);
      bottomPanel.Resize += BottomPanel_Resize;

      Controls.Add(mainPanel);
      Controls.Add(bottomPanel);

      AcceptButton = okButton;
      CancelButton = cancelButton;

      // ESC closes
      KeyPreview = true;
      KeyDown += (s, e) =>
      {
        if (e.KeyCode == Keys.Escape)
        {
          DialogResult = DialogResult.Cancel;
          Close();
        }
      };
    }

    private void BottomPanel_Resize(object sender, EventArgs e)
    {
      int spacing = 8;
      int right = bottomPanel.ClientSize.Width - spacing;

      if (cancelButton != null)
      {
        cancelButton.Location = new Point(
          right - cancelButton.Width,
          (bottomPanel.ClientSize.Height - cancelButton.Height) / 2
        );
        right = cancelButton.Left - spacing;
      }

      if (okButton != null)
      {
        okButton.Location = new Point(
          right - okButton.Width,
          (bottomPanel.ClientSize.Height - okButton.Height) / 2
        );
      }
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

  } // class MaterialAssetGridSelection


  public class MaterialActivatedEventArgs : EventArgs
  {
    public string MaterialKey { get; }

    public MaterialActivatedEventArgs(string key)
    {
      MaterialKey = key;
    }
  }


  internal class MaterialGridControl : Control
  {
    private readonly List<string> items = new List<string>();

    private int itemWidth = 160;
    private int itemHeight = 72;
    private int itemPadding = 10;

    private int scrollOffset;
    private CrownScrollBar vBar;

    private int hoverIndex = -1;
    private int selectedIndex = -1;

    private Image materialIcon;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MouseWheelScrollMultiplier { get; set; } = 48;

    public string SelectedMaterialKey
    {
      get
      {
        if (selectedIndex < 0 || selectedIndex >= items.Count)
        {
          return null;
        }
        return items[selectedIndex];
      }
    }

    public event EventHandler<MaterialActivatedEventArgs> ItemActivated;

    public MaterialGridControl()
    {
      SetStyle(
        ControlStyles.AllPaintingInWmPaint |
        ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.UserPaint |
        ControlStyles.ResizeRedraw |
        ControlStyles.Selectable,
        true
      );

      TabStop = true;
    }

    public void SetMaterials(IEnumerable<string> materials)
    {
      items.Clear();

      if (materials != null)
      {
        items.AddRange(materials.Where(m => !string.IsNullOrWhiteSpace(m)));
      }

      if (items.Count == 0)
      {
        selectedIndex = -1;
        hoverIndex = -1;
      }
      else if (selectedIndex >= items.Count)
      {
        selectedIndex = items.Count - 1;
      }

      scrollOffset = 0;
      UpdateScrollBar();
      Invalidate();
    }

    public void AttachScrollBar(CrownScrollBar bar)
    {
      if (vBar == bar)
      {
        return;
      }

      if (vBar != null)
      {
        vBar.ValueChanged -= ScrollBar_ValueChanged;
      }

      vBar = bar;

      if (vBar != null)
      {
        vBar.ScrollOrientation = ReaLTaiizor.Enum.Crown.ScrollOrientation.Vertical;
        vBar.ValueChanged += ScrollBar_ValueChanged;
        UpdateScrollBar();
      }
    }

    private void ScrollBar_ValueChanged(object sender, ScrollValueEventArgs e)
    {
      if (vBar == null)
      {
        return;
      }

      int maxValue = Math.Max(vBar.Minimum, vBar.Maximum - vBar.ViewSize);
      scrollOffset = Clamp(e.Value, vBar.Minimum, maxValue);
      Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);
      UpdateScrollBar();
      Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
      base.OnMouseWheel(e);

      if (vBar == null || !vBar.Enabled)
      {
        return;
      }

      int delta = e.Delta;
      if (delta == 0)
      {
        return;
      }

      int direction = delta > 0 ? -1 : 1;
      int change = MouseWheelScrollMultiplier;

      int newValue = scrollOffset + direction * change;

      int maxValue = Math.Max(vBar.Minimum, vBar.Maximum - vBar.ViewSize);
      newValue = Clamp(newValue, vBar.Minimum, maxValue);

      if (newValue != scrollOffset)
      {
        scrollOffset = newValue;
        vBar.Value = newValue;
        Invalidate();
      }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
      base.OnMouseMove(e);

      int idx = HitTest(e.Location);
      if (idx != hoverIndex)
      {
        hoverIndex = idx;
        Invalidate();
      }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
      base.OnMouseLeave(e);
      if (hoverIndex != -1)
      {
        hoverIndex = -1;
        Invalidate();
      }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
      base.OnMouseDown(e);

      Focus();

      if (e.Button == MouseButtons.Left)
      {
        int idx = HitTest(e.Location);
        if (idx >= 0 && idx < items.Count)
        {
          if (selectedIndex != idx)
          {
            selectedIndex = idx;
            Invalidate();
          }
        }
      }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
      base.OnMouseDoubleClick(e);

      if (e.Button == MouseButtons.Left)
      {
        int idx = HitTest(e.Location);
        if (idx >= 0 && idx < items.Count)
        {
          selectedIndex = idx;
          Invalidate();
          ActivateSelected();
        }
      }
    }

    protected override bool IsInputKey(Keys keyData)
    {
      if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)
      {
        return true;
      }
      return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
      base.OnKeyDown(e);

      if (items.Count == 0)
      {
        return;
      }

      int cols = ComputeColumns();
      if (cols <= 0) cols = 1;

      int idx = selectedIndex;
      if (idx < 0) idx = 0;

      bool changed = false;

      switch (e.KeyCode)
      {
        case Keys.Right:
        {
          if (idx + 1 < items.Count)
          {
            idx++;
            changed = true;
          }
          break;
        }

        case Keys.Left:
        {
          if (idx > 0)
          {
            idx--;
            changed = true;
          }
          break;
        }

        case Keys.Down:
        {
          if (idx + cols < items.Count)
          {
            idx += cols;
            changed = true;
          }
          break;
        }

        case Keys.Up:
        {
          if (idx - cols >= 0)
          {
            idx -= cols;
            changed = true;
          }
          break;
        }

        case Keys.Return:
        {
          ActivateSelected();
          e.Handled = true;
          return;
        }
      }

      if (changed)
      {
        selectedIndex = idx;
        EnsureIndexVisible(idx);
        Invalidate();
        e.Handled = true;
      }
    }

    private void ActivateSelected()
    {
      var key = SelectedMaterialKey;
      if (string.IsNullOrWhiteSpace(key))
      {
        return;
      }

      ItemActivated?.Invoke(this, new MaterialActivatedEventArgs(key));
    }

    private int HitTest(Point clientPoint)
    {
      if (items.Count == 0)
      {
        return -1;
      }

      int cols = ComputeColumns();
      if (cols <= 0)
      {
        return -1;
      }

      int x = clientPoint.X - itemPadding;
      if (x < 0)
      {
        return -1;
      }

      int stepX = itemWidth + itemPadding;
      int col = x / stepX;
      if (col < 0 || col >= cols)
      {
        return -1;
      }

      int y = clientPoint.Y + scrollOffset - itemPadding;
      if (y < 0)
      {
        return -1;
      }

      int stepY = itemHeight + itemPadding;
      int row = y / stepY;
      if (row < 0)
      {
        return -1;
      }

      int idx = row * cols + col;
      if (idx < 0 || idx >= items.Count)
      {
        return -1;
      }

      int tileX = itemPadding + col * stepX;
      int tileY = itemPadding + row * stepY - scrollOffset;
      Rectangle rect = new Rectangle(tileX, tileY, itemWidth, itemHeight);

      if (!rect.Contains(clientPoint))
      {
        return -1;
      }

      return idx;
    }

    private int ComputeColumns()
    {
      int width = ClientSize.Width;
      if (width <= 0)
      {
        return 1;
      }

      int step = itemWidth + itemPadding;
      if (step <= 0)
      {
        return 1;
      }

      return Math.Max(1, (width - itemPadding) / step);
    }

    private int ComputeTotalHeight()
    {
      if (items.Count == 0)
      {
        return ClientSize.Height;
      }

      int cols = ComputeColumns();
      if (cols <= 0) cols = 1;

      int rows = (int)Math.Ceiling(items.Count / (double)cols);
      if (rows < 1) rows = 1;

      return itemPadding + rows * (itemHeight + itemPadding);
    }

    private void EnsureIndexVisible(int index)
    {
      if (index < 0 || index >= items.Count)
      {
        return;
      }

      int cols = ComputeColumns();
      if (cols <= 0) cols = 1;

      int row = index / cols;
      int stepY = itemHeight + itemPadding;

      int top = itemPadding + row * stepY;
      int bottom = top + itemHeight;

      int viewTop = scrollOffset;
      int viewBottom = scrollOffset + ClientSize.Height;

      if (top < viewTop)
      {
        scrollOffset = top;
      }
      else if (bottom > viewBottom)
      {
        scrollOffset = bottom - ClientSize.Height;
      }

      if (vBar != null)
      {
        int maxValue = Math.Max(vBar.Minimum, vBar.Maximum - vBar.ViewSize);
        scrollOffset = Clamp(scrollOffset, vBar.Minimum, maxValue);
        vBar.Value = scrollOffset;
      }
      else
      {
        int max = Math.Max(0, ComputeTotalHeight() - ClientSize.Height);
        scrollOffset = Clamp(scrollOffset, 0, max);
      }
    }

    private void UpdateScrollBar()
    {
      if (vBar == null)
      {
        return;
      }

      int total = ComputeTotalHeight();
      int viewport = Math.Max(1, ClientSize.Height);

      vBar.Minimum = 0;
      vBar.Maximum = total;
      vBar.ViewSize = viewport;

      bool canScroll = total > viewport;

      vBar.Enabled = canScroll;
      vBar.Visible = canScroll;

      int maxValue = Math.Max(vBar.Minimum, vBar.Maximum - vBar.ViewSize);
      scrollOffset = Clamp(scrollOffset, vBar.Minimum, maxValue);
      vBar.Value = scrollOffset;

      Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      base.OnPaint(e);

      var g = e.Graphics;
      g.Clear(SwimEditorTheme.PageBg);

      if (items.Count == 0)
      {
        DrawEmptyMessage(g);
        return;
      }

      if (materialIcon == null)
      {
        materialIcon = CreateMaterialIcon();
      }

      int cols = ComputeColumns();
      if (cols <= 0) cols = 1;

      int stepX = itemWidth + itemPadding;
      int stepY = itemHeight + itemPadding;

      for (int i = 0; i < items.Count; i++)
      {
        int row = i / cols;
        int col = i % cols;

        int x = itemPadding + col * stepX;
        int y = itemPadding + row * stepY - scrollOffset;

        Rectangle tileRect = new Rectangle(x, y, itemWidth, itemHeight);

        if (tileRect.Bottom < 0 || tileRect.Top > ClientSize.Height)
        {
          continue;
        }

        DrawTile(g, tileRect, i, items[i]);
      }
    }

    private void DrawEmptyMessage(Graphics g)
    {
      string msg = "No materials registered.";
      var size = TextRenderer.MeasureText(msg, Font);
      int x = (ClientSize.Width - size.Width) / 2;
      int y = (ClientSize.Height - size.Height) / 2;

      TextRenderer.DrawText(
        g,
        msg,
        Font,
        new Point(Math.Max(0, x), Math.Max(0, y)),
        SwimEditorTheme.Text
      );
    }

    private void DrawTile(Graphics g, Rectangle rect, int index, string label)
    {
      bool isSelected = (index == selectedIndex);
      bool isHovered = (index == hoverIndex);

      Color border;
      Color fill;
      Color textColor = SwimEditorTheme.Text;

      if (isSelected)
      {
        fill = SwimEditorTheme.HoverColor;
        border = ControlPaint.Dark(SwimEditorTheme.HoverColor);
      }
      else if (isHovered)
      {
        fill = BlendColor(SwimEditorTheme.InputBg, SwimEditorTheme.HoverColor, 0.3f);
        border = ControlPaint.Dark(SwimEditorTheme.InputBg);
      }
      else
      {
        fill = SwimEditorTheme.InputBg;
        border = ControlPaint.Dark(SwimEditorTheme.InputBg);
      }

      Rectangle inner = Rectangle.Inflate(rect, -2, -2);

      using (var b = new SolidBrush(fill))
      {
        g.FillRectangle(b, inner);
      }

      using (var p = new Pen(border, 1f))
      {
        g.DrawRectangle(p, inner);
      }

      int iconSize = 32;
      int iconMargin = 8;
      Rectangle iconRect = new Rectangle(
        inner.Left + iconMargin,
        inner.Top + (inner.Height - iconSize) / 2,
        iconSize,
        iconSize
      );

      if (materialIcon != null)
      {
        g.DrawImage(materialIcon, iconRect);
      }

      int textLeft = iconRect.Right + iconMargin;
      Rectangle textRect = new Rectangle(
        textLeft,
        inner.Top + 4,
        inner.Right - textLeft - 4,
        inner.Height - 8
      );

      TextRenderer.DrawText(
        g,
        label,
        Font,
        textRect,
        textColor,
        TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter
      );
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

    private static int Clamp(int value, int min, int max)
    {
      if (value < min) return min;
      if (value > max) return max;
      return value;
    }

    private static Color BlendColor(Color a, Color b, float t)
    {
      t = Math.Max(0f, Math.Min(1f, t));
      int r = (int)Math.Round(a.R + (b.R - a.R) * t);
      int g = (int)Math.Round(a.G + (b.G - a.G) * t);
      int bb = (int)Math.Round(a.B + (b.B - a.B) * t);
      return Color.FromArgb(r, g, bb);
    }

  } // class MaterialGridControl

} // namespace SwimEditor
