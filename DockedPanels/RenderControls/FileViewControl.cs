using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SwimEditor
{

  /// <summary>
  /// Explorer-like browser: Tree (left) shows all drives and their folders/files.
  /// List (right) shows the currently selected folder (thumbnails for images).
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

    // Starting focus path (what the right side opens to on load / SetRoot)
    private string _rootPath;

    // Keep trees responsive on giant folders
    private const int MaxTreeChildrenPerFolder = 500;

    private const int largeSize = 48;
    private const int smallSize = 16;

    private static readonly string[] ImageExts = new[]
    {
      ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    // Layout prefs for initial left hierarchy width
    private const double LeftInitialPortion = 0.20; // % of control width at creation
    private const int LeftMinPixels = 220;  // min starting width

    private bool _layoutInitialized = false;
    private bool _userAdjustedSplitter = false;

    public FileViewControl()
    {
      BackColor = SwimEditorTheme.PageBg;

      _split = new SplitContainer
      {
        Dock = DockStyle.Fill,
        SplitterWidth = 4,
        FixedPanel = FixedPanel.None,
        // Start with something reasonable; will be recomputed to a percentage on handle/resize:
        SplitterDistance = 300,
        Panel1MinSize = LeftMinPixels
      };

      _tree = new TreeView
      {
        Dock = DockStyle.Fill,
        HideSelection = false,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        BorderStyle = BorderStyle.None,
        ShowRootLines = true
      };
      _tree.BeforeExpand += Tree_BeforeExpand;
      _tree.AfterSelect += (s, e) =>
      {
        if (e.Node != null && e.Node.Tag is NodeTag)
        {
          var tag = (NodeTag)e.Node.Tag;
          if (tag.IsDirectory && Directory.Exists(tag.Path))
            NavigateTo(tag.Path);
        }
      };
      _tree.NodeMouseDoubleClick += (s, e) =>
      {
        if (e.Node != null && e.Node.Tag is NodeTag)
        {
          var tag = (NodeTag)e.Node.Tag;
          if (!tag.IsDirectory && File.Exists(tag.Path))
            TryOpen(tag.Path);
        }
      };

      _largeImages = new ImageList { ImageSize = new Size(largeSize, largeSize), ColorDepth = ColorDepth.Depth32Bit };
      _smallImages = new ImageList { ImageSize = new Size(smallSize, smallSize), ColorDepth = ColorDepth.Depth32Bit };
      AddGenericIcons(_largeImages, _smallImages);

      _tree.ImageList = _smallImages;

      _list = new ListView
      {
        Dock = DockStyle.Fill,
        View = View.LargeIcon,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        BorderStyle = BorderStyle.None,
        LargeImageList = _largeImages,
        SmallImageList = _smallImages,
        AutoArrange = true,
        UseCompatibleStateImageBehavior = false
      };

      _list.MouseDoubleClick += (s, e) =>
      {
        var hit = _list.HitTest(e.Location);
        if (hit != null && hit.Item != null && hit.Item.Tag is string)
        {
          var path = (string)hit.Item.Tag;
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
          if (string.IsNullOrEmpty(current)) return;
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

      // --- Initial left width logic ---
      // 1) compute on first handle creation
      HandleCreated += (s, e) => ApplyInitialLeftWidth();
      // 2) if the control resizes BEFORE the user drags, keep it proportional
      Resize += (s, e) =>
      {
        if (!_userAdjustedSplitter)
          ApplyInitialLeftWidth();
      };
      // 3) once the user moves the splitter, stop overriding it
      _split.SplitterMoved += (s, e) =>
      {
        if (_layoutInitialized) _userAdjustedSplitter = true;
      };

      // Default starting focus = running binary directory
      SetRoot(AppDomain.CurrentDomain.BaseDirectory);
    }

    /// <summary>
    /// Sets the starting folder focus (right pane). Tree always spans ALL drives.
    /// </summary>
    public void SetRoot(string startPath)
    {
      _rootPath = startPath;
      BuildTreeForAllDrives();
      if (Directory.Exists(_rootPath))
        NavigateTo(_rootPath);
      else
      {
        // fall back: first ready drive root
        var first = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);
        if (first != null) NavigateTo(first.RootDirectory.FullName);
      }
    }

    /// <summary>
    /// Applies the initial left-panel width as a percentage of the control,
    /// clamped by a minimum pixel width. Stops re-applying once the user moves the splitter.
    /// </summary>
    private void ApplyInitialLeftWidth()
    {
      if (!IsHandleCreated || _split == null) return;

      int total = Math.Max(1, _split.ClientSize.Width);
      int min1 = Math.Max(0, _split.Panel1MinSize);
      int min2 = Math.Max(0, _split.Panel2MinSize);
      int splitterW = Math.Max(0, _split.SplitterWidth);

      // Compute desired width as a percentage of total
      int desired = Math.Max(LeftMinPixels, (int)Math.Round(total * LeftInitialPortion));

      // Compute the *maximum* allowed SplitterDistance
      int maxAllowed = Math.Max(min1, total - min2 - splitterW);

      // Handle very small total width edge cases
      if (total <= (min1 + min2 + splitterW))
        desired = min1;
      else
        desired = Clamp(desired, min1, maxAllowed);

      // Apply only if valid and different
      if (desired >= min1 && desired <= maxAllowed && _split.SplitterDistance != desired)
      {
        try
        {
          _split.SplitterDistance = desired;
        }
        catch
        {
          // fallback just in case (avoids crash)
          _split.SplitterDistance = min1;
        }
      }

      _layoutInitialized = true;
    }

    /// <summary>
    /// Helper for compatibility with pre-.NET Core versions
    /// </summary>
    private static int Clamp(int value, int min, int max)
    {
      if (value < min) return min;
      if (value > max) return max;
      return value;
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

      // keep tree selection synced
      SelectTreeNodeForPath(path);

      // If we're in LargeIcon view, recompute spacing so the grid is centered
      if (_list.View == View.LargeIcon)
        UpdateIconSpacingForCentering();
    }

    // ---------- internals ----------

    private void BuildTreeForAllDrives()
    {
      _tree.BeginUpdate();
      try
      {
        _tree.Nodes.Clear();

        // Optional "This PC" grouping node
        var root = new TreeNode("This PC")
        {
          ForeColor = SwimEditorTheme.Text,
          ImageKey = "COMPUTER",
          SelectedImageKey = "COMPUTER"
        };
        _tree.Nodes.Add(root);

        foreach (var di in DriveInfo.GetDrives())
        {
          // Only show drives that exist; show even if not ready (no media)
          var driveNode = MakeDriveNode(di);
          root.Nodes.Add(driveNode);

          // Ready drives get a placeholder so they can be expanded
          if (di.IsReady) AddPlaceholders(driveNode);
        }

        root.Expand();
      }
      finally
      {
        _tree.EndUpdate();
      }
    }

    private class NodeTag
    {
      public string Path;
      public bool IsDirectory;
      public NodeTag(string path, bool isDir) { Path = path; IsDirectory = isDir; }
    }

    private TreeNode MakeDriveNode(DriveInfo di)
    {
      string label;
      try
      {
        var vol = "";
        if (di.IsReady)
          vol = di.VolumeLabel;
        label = string.IsNullOrEmpty(vol)
          ? $"{di.Name.TrimEnd('\\')}"
          : $"{vol} ({di.Name.TrimEnd('\\')})";
      }
      catch
      {
        label = di.Name.TrimEnd('\\');
      }

      var node = new TreeNode(label)
      {
        Tag = new NodeTag(di.RootDirectory.FullName, isDir: true),
        ForeColor = SwimEditorTheme.Text,
        ImageKey = "DRIVE",
        SelectedImageKey = "DRIVE"
      };
      return node;
    }

    private TreeNode MakeDirNode(string path)
    {
      var name = Path.GetFileName(path);
      if (string.IsNullOrEmpty(name)) name = path;
      var node = new TreeNode(name)
      {
        Tag = new NodeTag(path, isDir: true),
        ForeColor = SwimEditorTheme.Text,
        ImageKey = "FOLDER",
        SelectedImageKey = "FOLDER"
      };
      return node;
    }

    private TreeNode MakeFileNode(string path)
    {
      var name = Path.GetFileName(path);
      var key = EnsureImageKeyForPath(path);
      var node = new TreeNode(name)
      {
        Tag = new NodeTag(path, isDir: false),
        ForeColor = SwimEditorTheme.Text,
        ImageKey = key,
        SelectedImageKey = key
      };
      return node;
    }

    /// <summary>Adds a dummy child so the node shows an expand arrow.</summary>
    private static void AddPlaceholder(TreeNode node)
    {
      if (node.Nodes.Count == 0)
        node.Nodes.Add(new TreeNode { Tag = null }); // dummy
    }

    private void AddPlaceholders(TreeNode node)
    {
      try
      {
        var tag = node.Tag as NodeTag;
        var path = tag != null ? tag.Path : null;
        if (string.IsNullOrEmpty(path)) return;

        bool hasChild =
          SafeEnum(() => Directory.EnumerateDirectories(path)).Take(1).Any() ||
          SafeEnum(() => Directory.EnumerateFiles(path)).Take(1).Any();

        if (hasChild) AddPlaceholder(node);
      }
      catch { /* ignore */ }
    }

    // C# 7/8-friendly version (no `is not`)
    private void Tree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
    {
      var tag = e.Node != null ? e.Node.Tag as NodeTag : null;
      if (tag == null || !tag.IsDirectory)
        return;

      // If this node still has the single dummy child, replace with actual content
      if (e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null)
      {
        PopulateDirectoryNode(e.Node, tag.Path);
      }
    }

    private void PopulateDirectoryNode(TreeNode node, string dirPath)
    {
      node.Nodes.Clear();

      int added = 0;

      // Subdirectories
      foreach (var dir in SafeEnum(() => Directory.EnumerateDirectories(dirPath)))
      {
        var child = MakeDirNode(dir);
        AddPlaceholders(child);
        node.Nodes.Add(child);
        added++;
        if (added >= MaxTreeChildrenPerFolder) break;
      }

      // Files (as leaf nodes)
      if (added < MaxTreeChildrenPerFolder)
      {
        foreach (var file in SafeEnum(() => Directory.EnumerateFiles(dirPath)))
        {
          var child = MakeFileNode(file);
          node.Nodes.Add(child);
          added++;
          if (added >= MaxTreeChildrenPerFolder) break;
        }
      }

      // Ellipsis node if we truncated
      int remaining =
        CountSafe(() => Directory.EnumerateDirectories(dirPath)) +
        CountSafe(() => Directory.EnumerateFiles(dirPath)) - added;

      if (remaining > 0)
      {
        var more = new TreeNode($"… ({remaining} more)") { ForeColor = Color.Gray };
        node.Nodes.Add(more);
      }
    }

    private void SelectTreeNodeForPath(string path)
    {
      TreeNode found = FindNodeByPath(_tree.Nodes, path);
      if (found != null)
      {
        _tree.SelectedNode = found;
        found.EnsureVisible();
      }
    }

    private TreeNode FindNodeByPath(TreeNodeCollection nodes, string path)
    {
      foreach (TreeNode n in nodes)
      {
        var tag = n.Tag as NodeTag;

        if (tag != null && tag.IsDirectory &&
            string.Equals(NormalizeDir(tag.Path), NormalizeDir(path), StringComparison.OrdinalIgnoreCase))
          return n;

        if (tag != null && tag.IsDirectory && PathStartsWith(path, tag.Path))
        {
          // Ensure children are populated before searching deeper
          if (n.Nodes.Count == 1 && n.Nodes[0].Tag == null)
            PopulateDirectoryNode(n, tag.Path);

          var child = FindNodeByPath(n.Nodes, path);
          if (child != null) return child;
        }

        // Recurse into "This PC"
        var childSearch = FindNodeByPath(n.Nodes, path);
        if (childSearch != null) return childSearch;
      }
      return null;
    }

    private static string NormalizeDir(string p)
    {
      if (string.IsNullOrEmpty(p)) return p;
      return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathStartsWith(string full, string prefix)
    {
      try
      {
        var f = Path.GetFullPath(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var p = Path.GetFullPath(prefix).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (p.Length == 2 && p[1] == ':') p += Path.DirectorySeparatorChar; // "C:" -> "C:\"
        return f.StartsWith(p, StringComparison.OrdinalIgnoreCase);
      }
      catch { return false; }
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
          using (var img = Image.FromStream(fs, false, false))
          {
            var thumb = CreateThumbnail(img, largeSize, largeSize);
            var small = new Bitmap(thumb, smallSize, smallSize);
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
      // Folder
      using (var bmpFolder = new Bitmap(largeSize * 2, largeSize * 2))
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
        large.Images.Add("FOLDER", (Bitmap)bmpFolder.Clone(new Rectangle(0, 0, largeSize * 2, largeSize * 2), bmpFolder.PixelFormat));
      }

      // File
      using (var bmpFile = new Bitmap(largeSize * 2, largeSize * 2))
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
        large.Images.Add("FILE", (Bitmap)bmpFile.Clone(new Rectangle(0, 0, largeSize * 2, largeSize * 2), bmpFile.PixelFormat));
      }

      // Drive
      using (var bmpDrive = new Bitmap(largeSize * 2, largeSize * 2))
      using (var g = Graphics.FromImage(bmpDrive))
      {
        g.Clear(Color.Transparent);
        using (var body = new SolidBrush(Color.Silver))
        using (var edge = new Pen(Color.DimGray, 6))
        {
          var r = new Rectangle(12, 28, 72, 40);
          g.FillRectangle(body, r);
          g.DrawRectangle(edge, r);
          g.FillRectangle(Brushes.DarkGray, new Rectangle(12, 60, 72, 12));
        }
        large.Images.Add("DRIVE", (Bitmap)bmpDrive.Clone(new Rectangle(0, 0, largeSize * 2, largeSize * 2), bmpDrive.PixelFormat));
      }

      // Computer (for "This PC")
      using (var bmpPc = new Bitmap(largeSize * 2, largeSize * 2))
      using (var g = Graphics.FromImage(bmpPc))
      {
        g.Clear(Color.Transparent);
        using (var scr = new SolidBrush(Color.LightSteelBlue))
        using (var edge = new Pen(Color.SteelBlue, 6))
        {
          g.FillRectangle(scr, new Rectangle(14, 12, 68, 44));
          g.DrawRectangle(edge, new Rectangle(14, 12, 68, 44));
          g.DrawLine(edge, 26, 62, 70, 62);
          g.DrawLine(edge, 30, 62, 30, 76);
          g.DrawLine(edge, 66, 62, 66, 76);
          g.DrawLine(edge, 30, 76, 66, 76);
        }
        large.Images.Add("COMPUTER", (Bitmap)bmpPc.Clone(new Rectangle(0, 0, largeSize * 2, largeSize * 2), bmpPc.PixelFormat));
      }

      // Small variants
      small.Images.Add("FOLDER", new Bitmap(large.Images["FOLDER"], smallSize, smallSize));
      small.Images.Add("FILE", new Bitmap(large.Images["FILE"], smallSize, smallSize));
      small.Images.Add("DRIVE", new Bitmap(large.Images["DRIVE"], smallSize, smallSize));
      small.Images.Add("COMPUTER", new Bitmap(large.Images["COMPUTER"], smallSize, smallSize));
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

    private static int CountSafe(Func<IEnumerable<string>> getter)
    {
      try { return getter()?.Count() ?? 0; }
      catch { return 0; }
    }

    // --- ListView large-icon centering machinery ---

    // Win32 interop: set icon spacing in LargeIcon/SmallIcon views.
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int LVM_FIRST = 0x1000;
    private const int LVM_SETICONSPACING = LVM_FIRST + 53;

    // Packs two 16-bit values into an IntPtr (low = x, high = y)
    private static IntPtr PackToLParam(int x, int y)
    {
      unchecked
      {
        int packed = (y << 16) | (x & 0xFFFF);
        return new IntPtr(packed);
      }
    }

    /// <summary>
    /// Computes and applies horizontal/vertical icon spacing so that the
    /// icon grid appears visually centered when there is extra width.
    /// </summary>
    private void UpdateIconSpacingForCentering()
    {
      if (!IsHandleCreated || !_list.IsHandleCreated) return;
      if (_list.View != View.LargeIcon) return;

      // Base cell size: image plus a bit of margin for text.
      //  - Horizontal: image width + ~ (text margin + inter-item gap)
      //  - Vertical:   image height + text line + gap
      // Tune these constants if you change image size or font.
      int imgW = _list.LargeImageList?.ImageSize.Width ?? largeSize;
      int imgH = _list.LargeImageList?.ImageSize.Height ?? largeSize;

      int baseCellW = imgW + largeSize;  // ~= left/right padding + gap between items
      int baseCellH = imgH + (largeSize - 12);  // ~= caption height + vertical gap

      int viewW = Math.Max(1, _list.ClientSize.Width);

      // How many columns would fit at the base spacing?
      int cols = Math.Max(1, viewW / baseCellW);

      // If only one column fits, we still want a little side padding:
      if (cols <= 1)
      {
        int cx = Math.Min(viewW - (smallSize / 2), baseCellW);
        int cy = baseCellH;
        SendMessage(_list.Handle, LVM_SETICONSPACING, IntPtr.Zero, PackToLParam(cx, cy));
        return;
      }

      // Distribute the leftover width as extra horizontal spacing
      int used = cols * baseCellW;
      int leftover = Math.Max(0, viewW - used);

      // Spread the leftover into (cols + 1) gaps; add half to each item cell to emulate centering.
      // We implement that by increasing the icon cell width (cx).
      int gap = leftover / (cols + 1);
      int cxFinal = baseCellW + gap;

      // Apply; cy stays constant.
      SendMessage(_list.Handle, LVM_SETICONSPACING, IntPtr.Zero, PackToLParam(cxFinal, baseCellH));
    }

  } // class FileViewControl

} // Namespace SwimEditor
