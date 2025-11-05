using ReaLTaiizor.Controls;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwimEditor
{

  internal static class ShellNative
  {
    [StructLayout(LayoutKind.Sequential)]
    public struct SHChangeNotifyEntry
    {
      public IntPtr pidl;
      [MarshalAs(UnmanagedType.Bool)]
      public bool fRecursive;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("shell32.dll")]
    public static extern IntPtr SHChangeNotifyRegister(
        IntPtr hwnd,
        int fSources,
        int fEvents,
        uint wMsg,
        int cEntries,
        [In] SHChangeNotifyEntry[] entries);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SHChangeNotifyDeregister(IntPtr hNotify);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    // fSources
    public const int SHCNRF_ShellLevel = 0x0002;
    public const int SHCNRF_InterruptLevel = 0x0001;
    public const int SHCNRF_NewDelivery = 0x8000;

    // fEvents
    public const int SHCNE_ALLEVENTS = 0x7FFFFFFF;
  }

  /// <summary>
  /// Explorer-like browser using CrownTreeView (left) + ListView (right).
  /// - Left shows all drives; expanding loads children on demand (skips symlinks/reparse)
  /// - Right shows current folder; double-click to enter / open
  /// - Selection stays in sync both ways with re-entrancy guards (no stack overflow)
  /// - Watches the active right-pane directory via SHChangeNotifyRegister and refreshes on shell message
  /// - Also refreshes the matching node in the left tree so directories stay visually in sync, preserving expansion and selection
  /// </summary>
  public class FileViewControl : UserControl
  {
    private readonly SplitContainer split;

    // LEFT: CrownTreeView
    private readonly CrownTreeView tree;

    // RIGHT: ListView + toolbar + path box
    private readonly ListView list;
    private readonly ImageList largeImages;
    private readonly ImageList smallImages;
    private readonly ToolStrip tool;
    private readonly TextBox pathBox;

    // Starting focus path (what the right side opens to on load / SetRoot)
    private string rootPath;

    // Keep trees responsive on giant folders
    private const int MaxTreeChildrenPerFolder = 500;

    private const int largeSize = 48;
    private const int smallSize = 16;

    private static readonly string[] ImageExts =
    {
      ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    // Layout prefs for initial left hierarchy width
    private const double LeftInitialPortion = 0.20; // % of control width at creation
    private const int LeftMinPixels = 220;          // min starting width

    private bool layoutInitialized = false;
    private bool userAdjustedSplitter = false;

    // Re-entrancy guards
    private bool suppressSelectionNavigate = false; // tree selection -> NavigateTo
    private bool suppressTreeSync = false;          // NavigateTo -> SelectTreeNodeForPath

    // Prevent re-entrancy when we force re-expand after populating
    private bool reexpandGuard = false;

    // --- Shell notify (message-only; no threads/timers) ---
    private IntPtr shNotifyHandle = IntPtr.Zero;
    private uint shNotifyMsg;
    private IntPtr currentPidl = IntPtr.Zero;

    public FileViewControl()
    {
      BackColor = SwimEditorTheme.PageBg;

      split = new SplitContainer
      {
        Dock = DockStyle.Fill,
        SplitterWidth = 4,
        FixedPanel = FixedPanel.None,
        SplitterDistance = 300,
        Panel1MinSize = LeftMinPixels
      };

      // CrownTreeView
      tree = new CrownTreeView
      {
        Dock = DockStyle.Fill,
        ShowIcons = true,
        MultiSelect = false
      };

      // Optional: keep this to ensure any already-expanded node with a placeholder gets populated.
      tree.AfterNodeExpand += (s, e) => PopulateAnyExpandedWithPlaceholder(tree.Nodes);

      // Selection -> sync right list (no expand/collapse on single click)
      tree.SelectedNodesChanged += (s, e) =>
      {
        var node = tree.SelectedNodes.LastOrDefault();
        if (node?.Tag is NodeTag tag && tag.IsDirectory && Directory.Exists(tag.Path))
        {
          // Just navigate the right pane; do NOT change node.Expanded here.
          NavigateTo(tag.Path);
        }
      };

      // Double-click in the tree = open (match right list behavior)
      tree.MouseDoubleClick += (s, e) =>
      {
        var node = tree.SelectedNodes.LastOrDefault();
        if (node?.Tag is NodeTag tag)
        {
          if (tag.IsDirectory && Directory.Exists(tag.Path))
          {
            // If it’s the first open, populate once here (so expand shows content immediately)
            if (node.Nodes.Count == 1 && node.Nodes[0].Tag == null)
              PopulateDirectoryNode(node, tag.Path);

            // Navigate right pane as well
            NavigateTo(tag.Path);
          }
          else if (!tag.IsDirectory && File.Exists(tag.Path))
          {
            TryOpen(tag.Path);
          }
        }
      };

      largeImages = new ImageList { ImageSize = new Size(largeSize, largeSize), ColorDepth = ColorDepth.Depth32Bit };
      smallImages = new ImageList { ImageSize = new Size(smallSize, smallSize), ColorDepth = ColorDepth.Depth32Bit };
      AddGenericIcons(largeImages, smallImages);

      // Right panel list
      list = new ListView
      {
        Dock = DockStyle.Fill,
        View = View.LargeIcon,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text,
        BorderStyle = BorderStyle.None,
        LargeImageList = largeImages,
        SmallImageList = smallImages,
        AutoArrange = true,
        UseCompatibleStateImageBehavior = false
      };

      list.MouseDoubleClick += (s, e) =>
      {
        var hit = list.HitTest(e.Location);
        if (hit?.Item?.Tag is string path)
        {
          if (Directory.Exists(path)) NavigateTo(path);
          else TryOpen(path);
        }
      };

      // Toolbar
      tool = new CrownToolStrip
      {
        GripStyle = ToolStripGripStyle.Hidden,
        BackColor = SwimEditorTheme.PageBg,

        // Important: stop 23x23 autosize and allow a taller row
        AutoSize = false,
        Stretch = true,
        Dock = DockStyle.Top,
        Padding = new Padding(4, 2, 4, 2),
        ImageScalingSize = new Size(20, 20),
        CanOverflow = false,
        Height = 32
      };

      var upBtn = new System.Windows.Forms.Button
      {
        Text = "Up Directory",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(2, 2, 2, 2),

        // flat + themed to avoid white outline
        FlatStyle = FlatStyle.Flat,
        TabStop = false,
        UseVisualStyleBackColor = false,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text
      };
      upBtn.FlatAppearance.BorderSize = 0;
      upBtn.FlatAppearance.BorderColor = SwimEditorTheme.PageBg;
      upBtn.FlatAppearance.MouseOverBackColor = SwimEditorTheme.HoverColor;
      upBtn.FlatAppearance.MouseDownBackColor = SwimEditorTheme.HoverColor;

      upBtn.Click += (s, e) =>
      {
        try
        {
          var current = list.Tag as string ?? rootPath;
          if (string.IsNullOrEmpty(current)) return;
          var parent = Directory.GetParent(current);
          if (parent != null) NavigateTo(parent.FullName);
        }
        catch { }
      };
      var upHost = new ToolStripControlHost(upBtn)
      {
        AutoSize = true,
        Margin = new Padding(8, 0, 0, 0)
      };

      // Place after View; host real Buttons to avoid ToolStrip 23px sizing quirks
      var openBtn = new System.Windows.Forms.Button
      {
        Text = "Open in Explorer",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(2, 2, 2, 2),

        // flat + themed to avoid white outline
        FlatStyle = FlatStyle.Flat,
        TabStop = false,
        UseVisualStyleBackColor = false,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text
      };
      openBtn.FlatAppearance.BorderSize = 0;
      openBtn.FlatAppearance.BorderColor = SwimEditorTheme.PageBg;
      openBtn.FlatAppearance.MouseOverBackColor = SwimEditorTheme.HoverColor;
      openBtn.FlatAppearance.MouseDownBackColor = SwimEditorTheme.HoverColor;

      openBtn.Click += (s, e) =>
      {
        try
        {
          var current = list.Tag as string ?? rootPath;
          if (string.IsNullOrEmpty(current) || !Directory.Exists(current)) return;

          System.Diagnostics.Process.Start(new ProcessStartInfo
          {
            FileName = "explorer.exe",
            Arguments = $"\"{current}\"",
            UseShellExecute = true
          });
        }
        catch { }
      };
      var openHost = new ToolStripControlHost(openBtn)
      {
        AutoSize = true,
        Margin = new Padding(8, 0, 0, 0)
      };

      // Return to Project (go back to the binary's main folder)
      var returnBtn = new System.Windows.Forms.Button
      {
        Text = "Return to Project",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(2, 2, 2, 2),

        FlatStyle = FlatStyle.Flat,
        TabStop = false,
        UseVisualStyleBackColor = false,
        BackColor = SwimEditorTheme.PageBg,
        ForeColor = SwimEditorTheme.Text
      };
      returnBtn.FlatAppearance.BorderSize = 0;
      returnBtn.FlatAppearance.BorderColor = SwimEditorTheme.PageBg;
      returnBtn.FlatAppearance.MouseOverBackColor = SwimEditorTheme.HoverColor;
      returnBtn.FlatAppearance.MouseDownBackColor = SwimEditorTheme.HoverColor;

      returnBtn.Click += (s, e) =>
      {
        try
        {
          var project = AppDomain.CurrentDomain.BaseDirectory;
          if (!string.IsNullOrEmpty(project) && Directory.Exists(project))
            NavigateTo(project);
        }
        catch { }
      };
      var returnHost = new ToolStripControlHost(returnBtn)
      {
        AutoSize = true,
        Margin = new Padding(8, 0, 0, 0)
      };

      tool.Items.Add(upHost);
      tool.Items.Add(openHost);
      tool.Items.Add(returnHost);

      // Path box
      pathBox = new CrownTextBox
      {
        Dock = DockStyle.Top,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = SwimEditorTheme.InputBg,
        ForeColor = SwimEditorTheme.Text
      };

      // --- Right side host with Crown scrollbars around the WinForms ListView ---
      var vBar = new CrownScrollBar { Dock = DockStyle.Right, Width = 16 };

      var rightContent = new ReaLTaiizor.Controls.Panel { Dock = DockStyle.Fill, BackColor = SwimEditorTheme.PageBg };
      rightContent.Controls.Add(list);   // ListView sits "under" the themed bars
      rightContent.Controls.Add(vBar);

      // Bind Crown scrollbars to ListView
      ListViewCrownScrollBinder.Attach(list, vBar);

      var rightPanel = new ReaLTaiizor.Controls.Panel { Dock = DockStyle.Fill, BackColor = SwimEditorTheme.PageBg };
      rightPanel.Controls.Add(rightContent);
      rightPanel.Controls.Add(pathBox);
      rightPanel.Controls.Add(tool);
      tool.Dock = DockStyle.Top;

      split.Panel1.Controls.Add(tree);
      split.Panel2.Controls.Add(rightPanel);
      Controls.Add(split);

      // --- Initial left width logic ---
      HandleCreated += (s, e) => ApplyInitialLeftWidth();
      Resize += (s, e) => { if (!userAdjustedSplitter) ApplyInitialLeftWidth(); };
      split.SplitterMoved += (s, e) => { if (layoutInitialized) userAdjustedSplitter = true; };

      // Unique message for this control to receive shell change notifications
      shNotifyMsg = ShellNative.RegisterWindowMessage("SWIMEDITOR_SHNOTIFY_" + Guid.NewGuid().ToString("N"));

      // Default starting focus = running binary directory
      SetRoot(AppDomain.CurrentDomain.BaseDirectory);
    }

    /// <summary>
    /// Sets the starting folder focus (right pane). Tree always spans ALL drives.
    /// Ensures the path is expanded/visible in the left tree.
    /// </summary>
    public void SetRoot(string startPath)
    {
      rootPath = startPath;
      BuildTreeForAllDrives();

      string target = Directory.Exists(rootPath) ? rootPath : DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady)?.RootDirectory.FullName;

      if (!string.IsNullOrEmpty(target))
      {
        EnsurePathVisible(target);   // programmatic tree select w/ guard inside
        NavigateTo(target);          // list refresh + guarded tree sync
      }
    }

    // Call this for every node you create (drive/folder/file).
    private void HookNode(CrownTreeNode node)
    {
      node.NodeExpanded += (s, e) =>
      {
        var n = (CrownTreeNode)s;

        if (n.Tag is NodeTag t && t.IsDirectory)
        {
          // If this was a placeholder, fill it once right when it expands.
          if (n.Nodes.Count == 1 && n.Nodes[0].Tag == null)
          {
            PopulateDirectoryNode(n, t.Path);

            // After we swap in real children, some trees lose the expanded visual state.
            // Force it to remain expanded exactly once to avoid the “click twice” issue.
            if (!reexpandGuard)
            {
              try
              {
                reexpandGuard = true;
                n.Expanded = true;   // keep it open now that it has real kids
              }
              finally
              {
                reexpandGuard = false;
              }
            }

            // Make sure it repaints with the new children immediately.
            tree.Invalidate();
          }
        }
      };

      node.NodeCollapsed += (s, e) =>
      {
        // No-op; we only care about fixing the expand timing.
      };
    }

    // ---------- Crown tree helpers ----------

    private class NodeTag
    {
      public string Path;
      public bool IsDirectory;
      public NodeTag(string path, bool isDir) { Path = path; IsDirectory = isDir; }
    }

    private void BuildTreeForAllDrives()
    {
      tree.Nodes.Clear();

      var root = new CrownTreeNode("This PC")
      {
        Tag = new NodeTag("", true),
        Icon = (Bitmap)smallImages.Images["COMPUTER"]
      };
      HookNode(root);
      tree.Nodes.Add(root);

      foreach (var di in DriveInfo.GetDrives())
      {
        var driveNode = MakeDriveNode(di);
        root.Nodes.Add(driveNode);

        // Show expand arrow (populate on-demand)
        AddPlaceholder(driveNode);
      }

      root.Expanded = true;
    }

    private CrownTreeNode MakeDriveNode(DriveInfo di)
    {
      string label;
      try
      {
        var vol = di.IsReady ? di.VolumeLabel : "";
        label = string.IsNullOrEmpty(vol)
          ? $"{di.Name.TrimEnd('\\')}"
          : $"{vol} ({di.Name.TrimEnd('\\')})";
      }
      catch
      {
        label = di.Name.TrimEnd('\\');
      }

      var node = new CrownTreeNode(label)
      {
        Tag = new NodeTag(di.RootDirectory.FullName, isDir: true),
        Icon = (Bitmap)smallImages.Images["DRIVE"]
      };

      HookNode(node);
      return node;
    }

    private CrownTreeNode MakeDirNode(string path)
    {
      var name = Path.GetFileName(path);
      if (string.IsNullOrEmpty(name)) name = path;

      var node = new CrownTreeNode(name)
      {
        Tag = new NodeTag(path, isDir: true),
        Icon = (Bitmap)smallImages.Images["FOLDER"]
      };

      HookNode(node);
      return node;
    }

    private CrownTreeNode MakeFileNode(string path)
    {
      var name = Path.GetFileName(path);
      var key = EnsureImageKeyForPath(path);
      return new CrownTreeNode(name)
      {
        Tag = new NodeTag(path, isDir: false),
        Icon = (Bitmap)smallImages.Images[key]
      };
    }

    /// <summary>Adds a dummy child so the node shows an expand arrow.</summary>
    private static void AddPlaceholder(CrownTreeNode node)
    {
      if (node.Nodes.Count == 0)
      {
        var dummy = new CrownTreeNode(string.Empty) { Tag = null };
        node.Nodes.Add(dummy);
      }
    }

    /// <summary>
    /// Populate a directory node with children (dirs + files), skipping only reparse points.
    /// </summary>
    private void PopulateDirectoryNode(CrownTreeNode node, string dirPath)
    {
      node.Nodes.Clear();

      if (!TryGetDirEntries(dirPath, out var dirs, out var files))
      {
        return;
      }

      foreach (var dir in dirs)
      {
        var child = MakeDirNode(dir);
        if (TryHasAnyChild(dir)) AddPlaceholder(child);
        node.Nodes.Add(child);
      }

      foreach (var file in files)
      {
        var child = MakeFileNode(file); // files don't expand; no need to hook here
        node.Nodes.Add(child);
      }
    }

    private static bool PathStartsWith(string fullPath, string prefixPath)
    {
      if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(prefixPath))
        return false;

      try
      {
        string full = Path.GetFullPath(fullPath)
                          .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string prefix = Path.GetFullPath(prefixPath)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // "C:" -> "C:\"
        if (prefix.Length == 2 && prefix[1] == ':')
          prefix += Path.DirectorySeparatorChar;

        // Exact folder match
        if (string.Equals(full, prefix, StringComparison.OrdinalIgnoreCase))
          return true;

        // Ensure prefix ends with separator, then compare
        if (!prefix.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
            !prefix.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
          prefix += Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Finds/creates all segments of a path inside the tree, expanding them; selects final node.
    /// Programmatic selection is guarded to avoid re-entrancy into NavigateTo.
    /// </summary>
    private void EnsurePathVisible(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) return;

      var root = tree.Nodes.FirstOrDefault(); // "This PC"
      if (root == null) return;

      // 1) Pick drive node
      var drive = DriveInfo.GetDrives()
                           .FirstOrDefault(d => PathStartsWith(path, d.RootDirectory.FullName));
      if (drive == null) return;

      var driveNode = root.Nodes.FirstOrDefault(n =>
      {
        if (n.Tag is NodeTag t) return SameDir(t.Path, drive.RootDirectory.FullName);
        return false;
      });

      if (driveNode == null) return;

      // Populate drive content if still placeholder
      if (driveNode.Nodes.Count == 1 && driveNode.Nodes[0].Tag == null)
        PopulateDirectoryNode(driveNode, drive.RootDirectory.FullName);

      driveNode.Expanded = true;

      // 2) Walk each directory segment relative to drive root
      string rel = GetRelativePathSafe(drive.RootDirectory.FullName, path);
      if (string.IsNullOrEmpty(rel))
      {
        ProgrammaticSelect(driveNode);
        return;
      }

      var parts = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                            StringSplitOptions.RemoveEmptyEntries);

      CrownTreeNode current = driveNode;
      string curPath = drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);

      foreach (var part in parts)
      {
        curPath = Path.Combine(curPath, part);

        // Populate current if still placeholder
        if (current.Nodes.Count == 1 && current.Nodes[0].Tag == null)
          PopulateDirectoryNode(current, (current.Tag as NodeTag)?.Path);

        // Find (or add) this segment
        var next = current.Nodes.FirstOrDefault(n =>
        {
          if (n.Tag is NodeTag t) return t.IsDirectory && SameDir(t.Path, curPath);
          return false;
        });

        if (next == null && Directory.Exists(curPath))
        {
          next = MakeDirNode(curPath);
          if (TryHasAnyChild(curPath)) AddPlaceholder(next);
          current.Nodes.Add(next);
        }

        if (next == null) break;

        current = next;
        current.Expanded = true;
      }

      ProgrammaticSelect(current ?? driveNode);
    }

    private void ProgrammaticSelect(CrownTreeNode node)
    {
      if (node == null) return;
      try
      {
        suppressSelectionNavigate = true;
        tree.SelectNode(node);
        tree.EnsureVisible();
      }
      finally
      {
        suppressSelectionNavigate = false;
      }
    }

    private static string GetRelativePathSafe(string root, string target)
    {
      try
      {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullTarget = Path.GetFullPath(target);
        if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return fullTarget.Substring(fullRoot.Length);
      }
      catch { return string.Empty; }
    }

    private static bool SameDir(string a, string b)
    {
      try
      {
        a = Path.GetFullPath(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        b = Path.GetFullPath(b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
      }
      catch { return false; }
    }

    private void PopulateAnyExpandedWithPlaceholder(IEnumerable<CrownTreeNode> nodes)
    {
      foreach (var n in nodes)
      {
        if (n?.Tag is NodeTag t && t.IsDirectory && n.Expanded)
        {
          if (n.Nodes.Count == 1 && n.Nodes[0].Tag == null)
            PopulateDirectoryNode(n, t.Path);
        }
        if (n != null && n.Nodes != null && n.Nodes.Count > 0)
          PopulateAnyExpandedWithPlaceholder(n.Nodes);
      }
    }

    // Safe, single-pass enumeration; skip reparse points (symlinks/junctions)
    private static bool TryGetDirEntries(string dirPath, out List<string> subDirs, out List<string> files)
    {
      subDirs = new List<string>();
      files = new List<string>();

      try
      {
        var di = new DirectoryInfo(dirPath);
        if (!di.Exists) return false;

        IEnumerable<string> dirEnum;
        try { dirEnum = Directory.EnumerateDirectories(dirPath); }
        catch { return false; }

        foreach (var d in dirEnum)
        {
          try
          {
            var child = new DirectoryInfo(d);
            if (!child.Exists) continue;

            var childAttr = child.Attributes;
            if ((childAttr & FileAttributes.ReparsePoint) != 0)
              continue;

            subDirs.Add(d);
            if (subDirs.Count >= MaxTreeChildrenPerFolder) break;
          }
          catch { /* skip */ }
        }

        if (subDirs.Count < MaxTreeChildrenPerFolder)
        {
          IEnumerable<string> fileEnum;
          try { fileEnum = Directory.EnumerateFiles(dirPath); }
          catch { fileEnum = Array.Empty<string>(); }

          foreach (var f in fileEnum)
          {
            try
            {
              files.Add(f);
              if (subDirs.Count + files.Count >= MaxTreeChildrenPerFolder) break;
            }
            catch { /* skip */ }
          }
        }

        return true;
      }
      catch
      {
        return false;
      }
    }

    private static bool TryHasAnyChild(string dirPath)
    {
      try
      {
        using var e = Directory.EnumerateFileSystemEntries(dirPath).GetEnumerator();
        return e.MoveNext();
      }
      catch
      {
        return false;
      }
    }

    private void SelectTreeNodeForPath(string path)
    {
      if (suppressTreeSync) return;
      EnsurePathVisible(path);
    }

    // ---------- Right-pane navigation ----------

    /// <summary>Navigates the right pane to a given path (must exist).</summary>
    public void NavigateTo(string path)
    {
      if (!Directory.Exists(path)) return;

      list.BeginUpdate();
      try
      {
        list.Items.Clear();
        list.Tag = path;
        pathBox.Text = path;

        IEnumerable<string> dirs = Array.Empty<string>();
        IEnumerable<string> files = Array.Empty<string>();
        try { dirs = Directory.EnumerateDirectories(path); } catch { }
        try { files = Directory.EnumerateFiles(path); } catch { }

        foreach (var dir in dirs)
        {
          try
          {
            var name = Path.GetFileName(dir);
            var item = new ListViewItem(name)
            {
              Tag = dir,
              ImageKey = "FOLDER"
            };
            if (list.View == View.Details)
            {
              item.SubItems.Add("Folder");
              item.SubItems.Add("");
              item.SubItems.Add(GetWriteTime(dir));
            }
            list.Items.Add(item);
          }
          catch { }
        }

        foreach (var file in files)
        {
          try
          {
            var name = Path.GetFileName(file);
            var key = EnsureImageKeyForPath(file);
            var item = new ListViewItem(name)
            {
              Tag = file,
              ImageKey = key
            };
            if (list.View == View.Details)
            {
              item.SubItems.Add(Path.GetExtension(file).ToUpperInvariant() + " File");
              item.SubItems.Add(GetFileSize(file));
              item.SubItems.Add(GetWriteTime(file));
            }
            list.Items.Add(item);
          }
          catch { }
        }
      }
      finally
      {
        list.EndUpdate();
      }

      // keep tree selection synced and expanded along the path (guarded)
      try
      {
        suppressTreeSync = true;
        SelectTreeNodeForPath(path);
      }
      finally
      {
        suppressTreeSync = false;
      }

      // Register for shell notifications on the current right-pane directory
      WatchRightPaneDirectory(path);

      if (list.View == View.LargeIcon)
        UpdateIconSpacingForCentering();
    }

    private string EnsureImageKeyForPath(string filePath)
    {
      var ext = Path.GetExtension(filePath).ToLowerInvariant();
      var key = filePath; // unique per file (cache)
      if (largeImages.Images.ContainsKey(key))
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
            largeImages.Images.Add(key, thumb);
            smallImages.Images.Add(key, small);
            return key;
          }
        }
        catch { /* fall through to generic */ }
      }

      largeImages.Images.Add(key, largeImages.Images["FILE"]);
      smallImages.Images.Add(key, smallImages.Images["FILE"]);
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
      catch { }
    }

    // --- ListView large-icon centering machinery ---

    [DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int LVMFIRST = 0x1000;
    private const int LVMSETICONSPACING = LVMFIRST + 53;

    private static IntPtr PackToLParam(int x, int y)
    {
      unchecked
      {
        int packed = (y << 16) | (x & 0xFFFF);
        return new IntPtr(packed);
      }
    }

    private void UpdateIconSpacingForCentering()
    {
      if (!IsHandleCreated || !list.IsHandleCreated) return;
      if (list.View != View.LargeIcon) return;

      int imgW = list.LargeImageList?.ImageSize.Width ?? largeSize;
      int imgH = list.LargeImageList?.ImageSize.Height ?? largeSize;

      int baseCellW = imgW + largeSize;              // ~= left/right padding + gap
      int baseCellH = imgH + (largeSize - 12);       // ~= caption + gap

      int viewW = Math.Max(1, list.ClientSize.Width);
      int cols = Math.Max(1, viewW / baseCellW);

      if (cols <= 1)
      {
        int cx = Math.Min(viewW - (smallSize / 2), baseCellW);
        int cy = baseCellH;
        SendMessage(list.Handle, LVMSETICONSPACING, IntPtr.Zero, PackToLParam(cx, cy));
        return;
      }

      int used = cols * baseCellW;
      int leftover = Math.Max(0, viewW - used);
      int gap = leftover / (cols + 1);
      int cxFinal = baseCellW + gap;

      SendMessage(list.Handle, LVMSETICONSPACING, IntPtr.Zero, PackToLParam(cxFinal, baseCellH));
    }

    /// <summary>
    /// Applies the initial left-panel width as a percentage of the control,
    /// clamped by a minimum pixel width. Stops re-applying once the user moves the splitter.
    /// </summary>
    private void ApplyInitialLeftWidth()
    {
      if (!IsHandleCreated || split == null) return;

      // Compute desired distance based on the *current* size.
      int total = Math.Max(1, split.ClientSize.Width);
      int min1 = Math.Max(0, split.Panel1MinSize);
      int min2 = Math.Max(0, split.Panel2MinSize);
      int splitterW = Math.Max(0, split.SplitterWidth);

      int desired = Math.Max(LeftMinPixels, (int)Math.Round(total * LeftInitialPortion));
      int maxAllowed = Math.Max(min1, total - min2 - splitterW);

      if (total <= (min1 + min2 + splitterW))
        desired = min1;
      else
        desired = Clamp(desired, min1, maxAllowed);

      // Only attempt if it would actually change something.
      if (desired >= min1 && desired <= maxAllowed && split.SplitterDistance != desired)
      {
        // Try once with the computed bounds; if layout shifted in-flight, recompute and try once more.
        if (!TrySetSplitterDistance(split, desired))
        {
          // As a last resort, use Panel1's minimum; this is guaranteed valid.
          if (!TrySetSplitterDistance(split, split.Panel1MinSize))
          {
            Debug.WriteLine("ApplyInitialLeftWidth: Failed to set SplitterDistance even to Panel1MinSize.");
          }
        }
      }

      layoutInitialized = true;
    }

    private static bool TrySetSplitterDistance(SplitContainer split, int requested)
    {
      // Refresh bounds in case layout changed since last read.
      int total = Math.Max(1, split.ClientSize.Width);
      int min1 = Math.Max(0, split.Panel1MinSize);
      int min2 = Math.Max(0, split.Panel2MinSize);
      int splitterW = Math.Max(0, split.SplitterWidth);

      // If the panels + splitter fully occupy the width, only min1 is valid.
      int maxAllowed = (total <= (min1 + min2 + splitterW))
                     ? min1
                     : Math.Max(min1, total - min2 - splitterW);

      int clamped = Clamp(requested, min1, maxAllowed);

      if (split.SplitterDistance == clamped)
        return true;

      try
      {
        split.SplitterDistance = clamped;
        return true;
      }
      catch (InvalidOperationException)
      {
        return false;
      }
      catch (ArgumentOutOfRangeException)
      {
        return false;
      }
    }

    private static int Clamp(int value, int min, int max)
    {
      if (value < min) return min;
      if (value > max) return max;
      return value;
    }

    // ---------- Shell registration + message handling ----------

    private void WatchRightPaneDirectory(string path)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
          UnregisterShellNotify();
          return;
        }

        // Ensure this control has a handle for message delivery
        if (!IsHandleCreated)
        {
          HandleCreated += (s, e) => WatchRightPaneDirectory(path);
          return;
        }

        // Re-register for the new path
        UnregisterShellNotify();

        if (!TryPathToPidl(path, out currentPidl))
          return;

        var entries = new ShellNative.SHChangeNotifyEntry[]
        {
          new ShellNative.SHChangeNotifyEntry { pidl = currentPidl, fRecursive = false }
        };

        int sources = ShellNative.SHCNRF_ShellLevel | ShellNative.SHCNRF_InterruptLevel | ShellNative.SHCNRF_NewDelivery;

        shNotifyHandle = ShellNative.SHChangeNotifyRegister(
          Handle,
          sources,
          ShellNative.SHCNE_ALLEVENTS,
          shNotifyMsg,
          entries.Length,
          entries
        );

        if (shNotifyHandle == IntPtr.Zero)
          ReleaseCurrentPidl();
      }
      catch { }
    }

    private void UnregisterShellNotify()
    {
      try
      {
        if (shNotifyHandle != IntPtr.Zero)
        {
          try { ShellNative.SHChangeNotifyDeregister(shNotifyHandle); } catch { }
          shNotifyHandle = IntPtr.Zero;
        }
      }
      finally
      {
        ReleaseCurrentPidl();
      }
    }

    private void ReleaseCurrentPidl()
    {
      try
      {
        if (currentPidl != IntPtr.Zero)
        {
          ShellNative.CoTaskMemFree(currentPidl);
          currentPidl = IntPtr.Zero;
        }
      }
      catch { }
    }

    private static bool TryPathToPidl(string path, out IntPtr pidl)
    {
      pidl = IntPtr.Zero;
      try
      {
        uint dummy;
        int hr = ShellNative.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out dummy);
        return hr == 0 && pidl != IntPtr.Zero;
      }
      catch
      {
        return false;
      }
    }

    protected override void WndProc(ref Message m)
    {
      if (m.Msg == shNotifyMsg)
      {
        try
        {
          var current = list.Tag as string;
          if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
          {
            // Refresh the right list (NavigateTo keeps tree selection synced)
            NavigateTo(current);

            // Refresh the matching left tree node, but preserve expansion and selection beneath it
            RefreshTreeForDirectory(current);
          }
        }
        catch { }
      }

      base.WndProc(ref m);
    }

    private void RefreshTreeForDirectory(string dir)
    {
      try
      {
        var node = FindExistingNodeForPath(dir);
        if (node == null || node.Tag is not NodeTag tag || !tag.IsDirectory)
          return;

        // Snapshot selection and expanded nodes within this subtree
        string selectedPath = (tree.SelectedNodes.LastOrDefault()?.Tag as NodeTag)?.Path ?? string.Empty;
        var expanded = new List<string>();
        CollectExpandedPaths(node, expanded);

        bool wasExpanded = node.Expanded;

        // Repopulate children
        PopulateDirectoryNode(node, tag.Path);
        if (TryHasAnyChild(tag.Path)) AddPlaceholder(node);

        // Restore expansion state
        node.Expanded = wasExpanded;
        RestoreExpandedPaths(node, expanded);

        // Restore selection if it was inside this subtree
        if (!string.IsNullOrEmpty(selectedPath) && PathStartsWith(selectedPath, tag.Path))
          EnsurePathVisible(selectedPath);

        tree.Invalidate();
      }
      catch { }
    }

    private void CollectExpandedPaths(CrownTreeNode rootNode, List<string> output)
    {
      if (rootNode == null || output == null) return;

      foreach (var n in rootNode.Nodes)
      {
        var child = n as CrownTreeNode;
        if (child?.Tag is NodeTag t && t.IsDirectory)
        {
          if (child.Expanded)
            output.Add(t.Path);

          // Recurse to capture deeper expanded nodes
          if (child.Nodes != null && child.Nodes.Count > 0)
            CollectExpandedPaths(child, output);
        }
      }
    }

    private void RestoreExpandedPaths(CrownTreeNode rootNode, List<string> expandedPaths)
    {
      if (rootNode == null || expandedPaths == null || expandedPaths.Count == 0) return;

      // Breadth-first restore to avoid flicker from deep re-expands
      var queue = new Queue<CrownTreeNode>();
      queue.Enqueue(rootNode);

      while (queue.Count > 0)
      {
        var cur = queue.Dequeue();
        foreach (var n in cur.Nodes)
        {
          var child = n as CrownTreeNode;
          if (child?.Tag is NodeTag t && t.IsDirectory)
          {
            if (expandedPaths.Any(p => SameDir(p, t.Path)))
              child.Expanded = true;

            if (child.Nodes != null && child.Nodes.Count > 0)
              queue.Enqueue(child);
          }
        }
      }
    }

    /// <summary>
    /// Finds a node for the given path using only existing nodes (no creation or selection changes).
    /// Returns null if not currently present in the tree (e.g., not expanded/visible).
    /// </summary>
    private CrownTreeNode FindExistingNodeForPath(string path)
    {
      if (string.IsNullOrWhiteSpace(path)) return null;
      var root = tree.Nodes.FirstOrDefault(); // "This PC"
      if (root == null || root.Nodes.Count == 0) return null;

      var drive = DriveInfo.GetDrives()
                           .FirstOrDefault(d => PathStartsWith(path, d.RootDirectory.FullName));
      if (drive == null) return null;

      var driveNode = root.Nodes.FirstOrDefault(n =>
      {
        if (n.Tag is NodeTag t) return SameDir(t.Path, drive.RootDirectory.FullName);
        return false;
      });
      if (driveNode == null) return null;

      if (SameDir(path, drive.RootDirectory.FullName))
        return driveNode;

      // If the drive node hasn't been populated yet, then the subtree isn't active in the UI.
      if (driveNode.Nodes.Count == 1 && driveNode.Nodes[0].Tag == null)
        return null;

      string rel = GetRelativePathSafe(drive.RootDirectory.FullName, path);
      if (string.IsNullOrEmpty(rel)) return driveNode;

      var parts = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                            StringSplitOptions.RemoveEmptyEntries);

      CrownTreeNode current = driveNode;
      string curPath = drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);

      foreach (var part in parts)
      {
        curPath = Path.Combine(curPath, part);

        // If this node has not yet been populated (placeholder), we consider it not active
        if (current.Nodes.Count == 1 && current.Nodes[0].Tag == null)
          return null;

        var next = current.Nodes.FirstOrDefault(n =>
        {
          if (n.Tag is NodeTag t) return t.IsDirectory && SameDir(t.Path, curPath);
          return false;
        });

        if (next == null)
          return null;

        current = next;
      }

      return current;
    }

    // --- Lifecycle: clean up shell registration ---

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        try { UnregisterShellNotify(); } catch { }
      }
      base.Dispose(disposing);
    }

  }

}
