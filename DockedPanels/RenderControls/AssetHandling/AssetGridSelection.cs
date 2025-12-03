using ReaLTaiizor.Controls;
using ReaLTaiizor.Util;
using System.ComponentModel;
using Button = System.Windows.Forms.Button;
using Panel = System.Windows.Forms.Panel;

namespace SwimEditor
{

  /// <summary>
  /// Base popup for selecting an asset key from a grid view.
  /// Derived classes provide asset list, icon drawing, and selection behavior.
  /// </summary>
  public abstract class AssetGridSelection : Form
  {
    private readonly CrownScrollBar vBar;

    private readonly Panel mainPanel;
    private readonly Panel bottomPanel;

    private readonly CrownTextBox searchBox;

    private readonly Button okButton;
    private readonly Button cancelButton;

    private readonly AssetGridControl grid;

    protected AssetGridSelection(string windowTitle)
    {
      Text = windowTitle;
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
      mainPanel = new Panel
      {
        Dock = DockStyle.Fill,
        BackColor = SwimEditorTheme.PageBg
      };

      grid = new AssetGridControl
      {
        Dock = DockStyle.Fill,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        MouseWheelScrollMultiplier = 48,

        GetIconFunc = GetAssetIcon,
        GetEmptyMessageFunc = GetEmptyMessage
      };

      vBar = new CrownScrollBar
      {
        Dock = DockStyle.Right,
        Width = 16,
        ScrollOrientation = ReaLTaiizor.Enum.Crown.ScrollOrientation.Vertical,
      };

      grid.AttachScrollBar(vBar);

      grid.ItemActivated += (s, e) =>
      {
        if (!string.IsNullOrWhiteSpace(e.Key))
        {
          OnAssetChosen(e.Key);
        }
      };

      mainPanel.Controls.Add(grid);
      mainPanel.Controls.Add(vBar);

      // Bottom panel with search + OK / Cancel buttons
      bottomPanel = new Panel
      {
        Dock = DockStyle.Bottom,
        Height = 40,
        Padding = new Padding(8, 4, 8, 4),
        BackColor = SwimEditorTheme.PageBg
      };

      searchBox = new CrownTextBox
      {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = SwimEditorTheme.InputBg,
        ForeColor = SwimEditorTheme.Text,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
      };

      searchBox.TextChanged += (s, e) =>
      {
        grid.SetFilter(searchBox.Text);
      };

      okButton = new Button
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
        var key = grid.SelectedKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
          OnAssetChosen(key);
        }
      };

      cancelButton = new Button
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

      // Add controls to bottom panel (order doesn't matter, layout handled in resize)
      bottomPanel.Controls.Add(searchBox);
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

    /// <summary>
    /// Refreshes the grid contents from the derived class's asset key source.
    /// Call this from the derived constructor or whenever the asset list changes.
    /// </summary>
    protected void RefreshItems()
    {
      IEnumerable<string> keys = GetAssetKeys() ?? Enumerable.Empty<string>();
      grid.SetItems(keys);
    }

    /// <summary>
    /// Derived classes provide the list of asset keys to display.
    /// </summary>
    protected abstract IEnumerable<string> GetAssetKeys();

    /// <summary>
    /// Derived classes handle what happens when an asset is chosen
    /// (double-click or OK button).
    /// </summary>
    protected abstract void OnAssetChosen(string assetKey);

    /// <summary>
    /// Derived classes can override to supply a custom icon per asset key.
    /// Return null for no icon.
    /// </summary>
    protected virtual Image GetAssetIcon(string assetKey)
    {
      return null;
    }

    /// <summary>
    /// Derived classes can override to provide a custom "empty" message.
    /// </summary>
    protected virtual string GetEmptyMessage()
    {
      return "No assets registered.";
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
        right = okButton.Left - spacing;
      }

      if (searchBox != null)
      {
        int left = bottomPanel.Padding.Left;
        int availableWidth = right - left;

        if (availableWidth < 50)
        {
          availableWidth = 50;
        }

        searchBox.Size = new Size(availableWidth, searchBox.Height);
        searchBox.Location = new Point(
          left,
          (bottomPanel.ClientSize.Height - searchBox.Height) / 2
        );
      }
    }

  } // class AssetGridSelection


  public class AssetActivatedEventArgs : EventArgs
  {
    public string Key { get; }

    public AssetActivatedEventArgs(string key)
    {
      Key = key;
    }
  }


  internal class AssetGridControl : Control
  {
    private readonly List<string> allItems = new List<string>();
    private readonly List<string> items = new List<string>();

    private int itemWidth = 160;
    private int itemHeight = 72;
    private int itemPadding = 10;

    private int scrollOffset;
    private CrownScrollBar vBar;

    private int hoverIndex = -1;
    private int selectedIndex = -1;

    private string filterText = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MouseWheelScrollMultiplier { get; set; } = 48;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, Image> GetIconFunc { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string> GetEmptyMessageFunc { get; set; }

    public string SelectedKey
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

    public event EventHandler<AssetActivatedEventArgs> ItemActivated;

    public AssetGridControl()
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

    public void SetItems(IEnumerable<string> assetKeys)
    {
      allItems.Clear();

      if (assetKeys != null)
      {
        allItems.AddRange(assetKeys.Where(m => !string.IsNullOrWhiteSpace(m)));
      }

      ApplyFilter();
    }

    public void SetFilter(string filter)
    {
      filterText = filter ?? string.Empty;
      ApplyFilter();
    }

    private void ApplyFilter()
    {
      string previousKey = SelectedKey;

      items.Clear();

      if (allItems.Count > 0)
      {
        if (string.IsNullOrWhiteSpace(filterText))
        {
          items.AddRange(allItems);
        }
        else
        {
          string f = filterText.Trim();
          items.AddRange(
            allItems.Where(m =>
              !string.IsNullOrEmpty(m) &&
              m.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
            )
          );
        }
      }

      if (items.Count == 0)
      {
        selectedIndex = -1;
        hoverIndex = -1;
      }
      else
      {
        if (!string.IsNullOrWhiteSpace(previousKey))
        {
          int idx = items.FindIndex(m => string.Equals(m, previousKey, StringComparison.Ordinal));
          if (idx >= 0)
          {
            selectedIndex = idx;
          }
          else if (selectedIndex >= items.Count)
          {
            selectedIndex = items.Count - 1;
          }
        }
        else if (selectedIndex >= items.Count)
        {
          selectedIndex = items.Count - 1;
        }

        if (selectedIndex < 0 && items.Count > 0)
        {
          selectedIndex = 0;
        }
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
      var key = SelectedKey;
      if (string.IsNullOrWhiteSpace(key))
      {
        return;
      }

      ItemActivated?.Invoke(this, new AssetActivatedEventArgs(key));
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
      string msg = GetEmptyMessageFunc != null ? GetEmptyMessageFunc() : "No assets registered.";
      if (string.IsNullOrEmpty(msg))
      {
        return;
      }

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

      // Icon on the left
      int iconSize = 32;
      int iconMargin = 8;
      Rectangle iconRect = new Rectangle(
        inner.Left + iconMargin,
        inner.Top + (inner.Height - iconSize) / 2,
        iconSize,
        iconSize
      );

      Image icon = null;
      if (GetIconFunc != null)
      {
        icon = GetIconFunc(label);
      }

      if (icon != null)
      {
        g.DrawImage(icon, iconRect);
      }

      // Text to the right of icon
      int textLeft = iconRect.Right + iconMargin;
      Rectangle textRect = new Rectangle(
        textLeft,
        inner.Top + 4,
        inner.Right - textLeft - 4,
        inner.Height - 8
      );

      // Allow up to 4 lines of text inside the tile
      DrawWrappedLabel(g, label, textRect, textColor, 4);
    }

    /// <summary>
    /// Draws label as up to maxLines lines, wrapping on separators
    /// (space, '/', '\\', '_', '-'). Last line gets ellipsis if it still overflows.
    /// </summary>
    private void DrawWrappedLabel(Graphics g, string label, Rectangle rect, Color color, int maxLines)
    {
      if (string.IsNullOrEmpty(label) || maxLines <= 0)
      {
        return;
      }

      List<string> lines;
      BuildMultiLineLabel(g, label, rect.Width, maxLines, out lines);

      if (lines == null || lines.Count == 0)
      {
        return;
      }

      var flagsMeasure = TextFormatFlags.NoPadding | TextFormatFlags.Left;

      int lineHeight = TextRenderer.MeasureText(
        g,
        "Ag",
        Font,
        new Size(rect.Width, int.MaxValue),
        flagsMeasure
      ).Height;

      int totalHeight = lineHeight * lines.Count;

      int y = rect.Top + (rect.Height - totalHeight) / 2;
      if (y < rect.Top)
      {
        y = rect.Top;
      }

      Rectangle lineRect = new Rectangle(rect.Left, y, rect.Width, lineHeight);
      var flagsDraw = TextFormatFlags.NoPrefix | TextFormatFlags.Left;

      for (int i = 0; i < lines.Count; i++)
      {
        TextRenderer.DrawText(
          g,
          lines[i],
          Font,
          lineRect,
          color,
          flagsDraw
        );

        lineRect.Y += lineHeight;
      }
    }

    /// <summary>
    /// Splits label into up to maxLines lines that fit within maxWidth,
    /// preferring to break on separators; last line gets "..." if needed.
    /// </summary>
    private void BuildMultiLineLabel(Graphics g, string label, int maxWidth, int maxLines, out List<string> lines)
    {
      lines = new List<string>();
      if (string.IsNullOrEmpty(label) || maxLines <= 0)
      {
        return;
      }

      var flags = TextFormatFlags.NoPadding | TextFormatFlags.Left;

      string remaining = label.Trim();

      Size Measure(string text)
      {
        return TextRenderer.MeasureText(
          g,
          text,
          Font,
          new Size(int.MaxValue, int.MaxValue),
          flags
        );
      }

      for (int lineIndex = 0; lineIndex < maxLines && !string.IsNullOrEmpty(remaining); lineIndex++)
      {
        bool isLast = (lineIndex == maxLines - 1);

        // Last line: just fit what remains, with ellipsis if needed.
        if (isLast)
        {
          string cur = remaining;

          Size sFull = Measure(cur);
          if (sFull.Width <= maxWidth)
          {
            lines.Add(cur);
            break;
          }

          string ellipsis = "...";

          for (int len = cur.Length; len > 0; len--)
          {
            string candidate = cur.Substring(0, len) + ellipsis;
            Size s = Measure(candidate);

            if (s.Width <= maxWidth)
            {
              lines.Add(candidate);
              return;
            }
          }

          lines.Add(ellipsis);
          return;
        }

        // Non-last line: find a break position that fits this width.
        string work = remaining;
        Size fullSize = Measure(work);

        // If everything fits on this line and we still have lines left,
        // just put it all here and stop.
        if (fullSize.Width <= maxWidth)
        {
          lines.Add(work);
          break;
        }

        List<int> breaks = new List<int>();

        for (int i = 0; i < work.Length; i++)
        {
          char c = work[i];
          if (c == ' ' || c == '/' || c == '\\' || c == '_' || c == '-')
          {
            breaks.Add(i + 1);
          }
        }

        int breakPos = -1;

        if (breaks.Count > 0)
        {
          foreach (int pos in breaks)
          {
            string candidate = work.Substring(0, pos);
            Size s = Measure(candidate);

            if (s.Width <= maxWidth)
            {
              breakPos = pos;
            }
            else
            {
              break;
            }
          }

          if (breakPos <= 0)
          {
            breakPos = breaks[0];
          }
        }
        else
        {
          double ratio = maxWidth / (double)fullSize.Width;
          int approxChars = Math.Max(1, (int)Math.Floor(work.Length * ratio));

          if (approxChars >= work.Length)
          {
            approxChars = work.Length - 1;
          }

          breakPos = approxChars;
        }

        // Make break one character more conservative to avoid edge clipping.
        if (breakPos > 1)
        {
          breakPos--;
        }

        if (breakPos <= 0 || breakPos >= work.Length)
        {
          // Fallback: treat this as the last line with ellipsis.
          string cur = work;
          string ellipsis = "...";

          for (int len = cur.Length; len > 0; len--)
          {
            string candidate = cur.Substring(0, len) + ellipsis;
            Size s = Measure(candidate);

            if (s.Width <= maxWidth)
            {
              lines.Add(candidate);
              return;
            }
          }

          lines.Add(ellipsis);
          return;
        }

        string line = work.Substring(0, breakPos);
        // Ensure it actually fits, backing off if needed.
        while (line.Length > 1 && Measure(line).Width > maxWidth)
        {
          breakPos--;
          if (breakPos <= 0)
          {
            break;
          }
          line = work.Substring(0, breakPos);
        }

        line = line.TrimEnd();
        if (line.Length == 0)
        {
          // Safety: avoid infinite loop, push ellipsis and bail.
          lines.Add("...");
          return;
        }

        lines.Add(line);

        if (breakPos >= work.Length)
        {
          break;
        }

        remaining = work.Substring(breakPos).TrimStart();
      }
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

  } // class AssetGridControl

} // namespace SwimEditor
