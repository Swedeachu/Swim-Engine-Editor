using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SwimEditor
{

  /// <summary>
  /// Tree (left) + List (right) file browser rooted at a given path.
  /// Thumbnails for images; generic icons otherwise.
  /// </summary>
  public class FileViewControl : UserControl
  {
    private readonly SplitContainer _split;
    private readonly TreeView _tree;
    private readonly ListView _list;
    private readonly ImageList _largeImages;
    private readonly ImageList _smallImages;
    private readonly ToolStrip _tool;
    private readonly TextBox _pathBox;

    private string _rootPath;

    private static readonly string[] ImageExts = new[]
    {
      ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    public FileViewControl()
    {
      BackColor = SwimEditorTheme.PageBg;

      _split = new SplitContainer
      {
        Dock = DockStyle.Fill,
        SplitterWidth = 4
      };

      _tree = new TreeView
      {
        Dock = DockStyle.Fill,
        HideSelection = false,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        BorderStyle = BorderStyle.None
      };
      _tree.BeforeExpand += Tree_BeforeExpand;
      _tree.AfterSelect += (s, e) =>
      {
        if (e.Node?.Tag is string path && Directory.Exists(path))
          NavigateTo(path);
      };

      _largeImages = new ImageList { ImageSize = new Size(96, 96), ColorDepth = ColorDepth.Depth32Bit };
      _smallImages = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
      AddGenericIcons(_largeImages, _smallImages);

      _list = new ListView
      {
        Dock = DockStyle.Fill,
        View = View.LargeIcon,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        BorderStyle = BorderStyle.None,
        LargeImageList = _largeImages,
        SmallImageList = _smallImages
      };
      _list.MouseDoubleClick += (s, e) =>
      {
        var hit = _list.HitTest(e.Location);
        if (hit?.Item?.Tag is string path)
        {
          if (Directory.Exists(path))
            NavigateTo(path);
          else
            TryOpen(path);
        }
      };

      _tool = new ToolStrip
      {
        GripStyle = ToolStripGripStyle.Hidden,
        Renderer = new ToolStripProfessionalRenderer(),
        BackColor = SwimEditorTheme.PageBg
      };

      var upBtn = new ToolStripButton("Up");
      upBtn.Click += (s, e) =>
      {
        try
        {
          var current = _list.Tag as string ?? _rootPath;
          var parent = Directory.GetParent(current);
          if (parent != null)
            NavigateTo(parent.FullName);
        }
        catch { /* ignore */ }
      };

      var viewBtn = new ToolStripDropDownButton("View");
      var large = new ToolStripMenuItem("Large Icons") { Checked = true };
      var details = new ToolStripMenuItem("Details");

      large.Click += (s, e) =>
      {
        _list.View = View.LargeIcon;
        large.Checked = true; details.Checked = false;
      };
      details.Click += (s, e) =>
      {
        _list.View = View.Details;
        if (_list.Columns.Count == 0)
        {
          _list.Columns.Add("Name", 300);
          _list.Columns.Add("Type", 120);
          _list.Columns.Add("Size", 100, HorizontalAlignment.Right);
          _list.Columns.Add("Modified", 160);
        }
        large.Checked = false; details.Checked = true;
      };

      _tool.Items.Add(upBtn);
      _tool.Items.Add(viewBtn);
      viewBtn.DropDownItems.Add(large);
      viewBtn.DropDownItems.Add(details);

      _pathBox = new TextBox
      {
        Dock = DockStyle.Top,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = SwimEditorTheme.InputBg,
        ForeColor = SwimEditorTheme.Text
      };

      var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = SwimEditorTheme.PageBg };
      rightPanel.Controls.Add(_list);
      rightPanel.Controls.Add(_pathBox);
      rightPanel.Controls.Add(_tool);
      _tool.Dock = DockStyle.Top;

      _split.Panel1.Controls.Add(_tree);
      _split.Panel2.Controls.Add(rightPanel);
      Controls.Add(_split);

      // default root = running binary directory
      SetRoot(AppDomain.CurrentDomain.BaseDirectory);
    }

    /// <summary>Sets the root directory and rebuilds the tree.</summary>
    public void SetRoot(string path)
    {
      _rootPath = path;
      BuildTree();
      NavigateTo(_rootPath);
    }

    /// <summary>Navigates the right pane to a given path (must exist).</summary>
    public void NavigateTo(string path)
    {
      if (!Directory.Exists(path)) return;

      _list.BeginUpdate();
      try
      {
        _list.Items.Clear();
        _list.Tag = path;
        _pathBox.Text = path;

        // Folders first
        foreach (var dir in SafeEnum(() => Directory.EnumerateDirectories(path)))
        {
          var name = Path.GetFileName(dir);
          var item = new ListViewItem(name)
          {
            Tag = dir,
            ImageKey = "FOLDER"
          };
          if (_list.View == View.Details)
          {
            item.SubItems.Add("Folder");
            item.SubItems.Add("");
            item.SubItems.Add(GetWriteTime(dir));
          }
          _list.Items.Add(item);
        }

        // Files
        foreach (var file in SafeEnum(() => Directory.EnumerateFiles(path)))
        {
          var name = Path.GetFileName(file);
          var key = EnsureImageKeyForPath(file);
          var item = new ListViewItem(name)
          {
            Tag = file,
            ImageKey = key
          };
          if (_list.View == View.Details)
          {
            item.SubItems.Add(Path.GetExtension(file).ToUpperInvariant() + " File");
            item.SubItems.Add(GetFileSize(file));
            item.SubItems.Add(GetWriteTime(file));
          }
          _list.Items.Add(item);
        }
      }
      finally
      {
        _list.EndUpdate();
      }

      // ensure tree selection follows
      SelectTreeNodeForPath(path);
    }

    // ---------- internals ----------

    private void BuildTree()
    {
      _tree.Nodes.Clear();
      var rootNode = MakeDirNode(_rootPath);
      _tree.Nodes.Add(rootNode);
      TryPopulateChildPlaceholders(rootNode);
      rootNode.Expand();
      _tree.SelectedNode = rootNode;
    }

    private TreeNode MakeDirNode(string path)
    {
      var name = Path.GetFileName(path);
      if (string.IsNullOrEmpty(name)) name = path;
      return new TreeNode(name) { Tag = path, ForeColor = SwimEditorTheme.Text };
    }

    private void TryPopulateChildPlaceholders(TreeNode node)
    {
      node.Nodes.Clear();
      try
      {
        var path = node.Tag as string;
        foreach (var dir in Directory.EnumerateDirectories(path))
        {
          var child = MakeDirNode(dir);
          child.Nodes.Add(new TreeNode()); // dummy so it shows expandable
          node.Nodes.Add(child);
        }
      }
      catch { /* ignore inaccessible */ }
    }

    private void Tree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
    {
      if (e.Node?.Tag is string)
        TryPopulateChildPlaceholders(e.Node);
    }

    private void SelectTreeNodeForPath(string path)
    {
      // shallow sync (no heavy recursion)
      foreach (TreeNode n in _tree.Nodes)
      {
        if (n.Tag as string == path) { _tree.SelectedNode = n; return; }
        foreach (TreeNode c in n.Nodes)
        {
          if (c.Tag as string == path) { _tree.SelectedNode = c; return; }
        }
      }
    }

    private string EnsureImageKeyForPath(string filePath)
    {
      var ext = Path.GetExtension(filePath).ToLowerInvariant();
      var key = filePath; // unique per file (cache)
      if (_largeImages.Images.ContainsKey(key))
        return key;

      if (ImageExts.Contains(ext))
      {
        try
        {
          using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
          using (var img = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false))
          {
            var thumb = CreateThumbnail(img, 96, 96);
            var small = new Bitmap(thumb, 16, 16);
            _largeImages.Images.Add(key, thumb);
            _smallImages.Images.Add(key, small);
            return key;
          }
        }
        catch { /* fall through to generic */ }
      }

      _largeImages.Images.Add(key, _largeImages.Images["FILE"]);
      _smallImages.Images.Add(key, _smallImages.Images["FILE"]);
      return key;
    }

    private static Bitmap CreateThumbnail(Image img, int maxW, int maxH)
    {
      var scale = Math.Min((double)maxW / img.Width, (double)maxH / img.Height);
      if (scale <= 0) scale = 1;
      int w = Math.Max(1, (int)Math.Round(img.Width * scale));
      int h = Math.Max(1, (int)Math.Round(img.Height * scale));

      var bmp = new Bitmap(maxW, maxH);
      using (var g = Graphics.FromImage(bmp))
      {
        g.Clear(Color.Transparent);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        var x = (maxW - w) / 2;
        var y = (maxH - h) / 2;
        g.DrawImage(img, new Rectangle(x, y, w, h));
      }
      return bmp;
    }

    private static void AddGenericIcons(ImageList large, ImageList small)
    {
      using (var bmpFolder = new Bitmap(96, 96))
      using (var g = Graphics.FromImage(bmpFolder))
      {
        g.Clear(Color.Transparent);
        using (var pen = new Pen(Color.Goldenrod, 6))
        using (var fill = new SolidBrush(Color.Khaki))
        {
          var r = new Rectangle(10, 30, 76, 50);
          g.FillRectangle(fill, r);
          g.DrawRectangle(pen, r);
          g.FillRectangle(fill, new Rectangle(10, 20, 36, 20));
        }
        large.Images.Add("FOLDER", (Bitmap)bmpFolder.Clone(new Rectangle(0, 0, 96, 96), bmpFolder.PixelFormat));
      }

      using (var bmpFile = new Bitmap(96, 96))
      using (var g = Graphics.FromImage(bmpFile))
      {
        g.Clear(Color.Transparent);
        using (var pen = new Pen(Color.SlateGray, 6))
        using (var fill = new SolidBrush(Color.LightGray))
        {
          var r = new Rectangle(18, 10, 60, 76);
          g.FillRectangle(fill, r);
          g.DrawRectangle(pen, r);
          g.DrawLine(pen, 18, 26, 78, 26);
          g.DrawLine(pen, 18, 46, 78, 46);
          g.DrawLine(pen, 18, 66, 78, 66);
        }
        large.Images.Add("FILE", (Bitmap)bmpFile.Clone(new Rectangle(0, 0, 96, 96), bmpFile.PixelFormat));
      }

      small.Images.Add("FOLDER", new Bitmap(large.Images["FOLDER"], 16, 16));
      small.Images.Add("FILE", new Bitmap(large.Images["FILE"], 16, 16));
    }

    private static string GetWriteTime(string path)
    {
      try { return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"); }
      catch { return ""; }
    }

    private static string GetFileSize(string file)
    {
      try
      {
        var len = new FileInfo(file).Length;
        string[] units = { "B", "KB", "MB", "GB" };
        double size = len;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.#} {units[u]}";
      }
      catch { return ""; }
    }

    private static void TryOpen(string path)
    {
      try
      {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = path,
          UseShellExecute = true
        });
      }
      catch { /* ignore */ }
    }

    private static IEnumerable<string> SafeEnum(Func<IEnumerable<string>> getter)
    {
      try { return getter() ?? Enumerable.Empty<string>(); }
      catch { return Enumerable.Empty<string>(); }
    }
  }

}
