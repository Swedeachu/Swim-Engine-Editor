using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SwimEditor
{

  /// <summary>
  /// Custom dark-themed TreeView with integrated DarkScrollBars (vertical and horizontal).
  /// Hides the native scrollbars and uses custom scrollbars.
  /// Vertical scrollbar takes full height, horizontal scrollbar excludes vertical scrollbar area.
  /// </summary>
  public class DarkTreeView : TreeView
  {
    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr hWnd, int nBar);

    [DllImport("user32.dll")]
    private static extern int SetScrollPos(IntPtr hWnd, int nBar, int nPos, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern bool GetScrollRange(IntPtr hWnd, int nBar, out int lpMinPos, out int lpMaxPos);

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int SB_HORZ = 0;
    private const int SB_VERT = 1;
    private const int SB_BOTH = 3;
    private const int WM_HSCROLL = 0x0114;
    private const int WM_VSCROLL = 0x0115;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_SIZE = 0x0005;
    private const int WM_STYLECHANGED = 0x007D;
    private const int SB_THUMBPOSITION = 4;

    // Window style flags to proactively suppress native scrollbars at creation time.
    private const int WS_HSCROLL = 0x00100000;
    private const int WS_VSCROLL = 0x00200000;

    private DarkScrollBar vScrollBar;
    private DarkScrollBar hScrollBar;
    private Panel vScrollBarHost;
    private Panel hScrollBarHost;
    private bool suppressScrollSync = false;

    public DarkTreeView()
    {
      // TreeView styling
      BorderStyle = BorderStyle.None;
      BackColor = SwimEditorTheme.PageBg;
      ForeColor = SwimEditorTheme.Fg;
      LineColor = SwimEditorTheme.Line;

      // Create host panel for vertical scrollbar (full height, docked right)
      vScrollBarHost = new Panel
      {
        Dock = DockStyle.Right,
        Width = 16,
        BackColor = SwimEditorTheme.PageBg
      };

      // Create vertical scrollbar
      vScrollBar = new DarkScrollBar
      {
        Dock = DockStyle.Fill,
        AutoHide = true,
        Width = 16
      };

      // Create host panel for horizontal scrollbar (docked bottom)
      hScrollBarHost = new Panel
      {
        Dock = DockStyle.Bottom,
        Height = 16,
        BackColor = SwimEditorTheme.PageBg
      };

      // Create horizontal scrollbar
      hScrollBar = new DarkScrollBar
      {
        Dock = DockStyle.Fill,
        AutoHide = true,
        Height = 16,
        Orientation = ScrollOrientation.HorizontalScroll
      };

      // Wire up events
      vScrollBar.ScrollValueChanged += OnVScrollBarValueChanged;
      hScrollBar.ScrollValueChanged += OnHScrollBarValueChanged;

      // Add scrollbars to hosts
      vScrollBarHost.Controls.Add(vScrollBar);
      hScrollBarHost.Controls.Add(hScrollBar);

      // Add hosts to tree - ORDER MATTERS!
      // Add horizontal first (bottom), then vertical (right)
      // This way vertical will extend to full height, and horizontal will stop before vertical
      Controls.Add(hScrollBarHost);
      Controls.Add(vScrollBarHost);

      // Hook the scrollbars to this control
      vScrollBar.SetScrollHooks(this, vScrollBarHost, SyncVScrollBarToTree);
      hScrollBar.SetScrollHooks(this, hScrollBarHost, SyncHScrollBarToTree);
    }

    /// <summary>
    /// Proactively remove native scroll styles on creation to prevent OS from showing them.
    /// </summary>
    protected override CreateParams CreateParams
    {
      get
      {
        var cp = base.CreateParams;
        cp.Style &= ~WS_VSCROLL;
        cp.Style &= ~WS_HSCROLL;
        return cp;
      }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
      base.OnHandleCreated(e);
      HideNativeScrollBars();
      UpdateScrollBars();
    }

    private void HideNativeScrollBars()
    {
      if (IsHandleCreated)
      {
        // Ensure the native scrollbars are hidden
        ShowScrollBar(Handle, SB_BOTH, false);
      }
    }

    protected override void OnAfterExpand(TreeViewEventArgs e)
    {
      base.OnAfterExpand(e);
      HideNativeScrollBars();
      UpdateScrollBars();
    }

    protected override void OnAfterCollapse(TreeViewEventArgs e)
    {
      base.OnAfterCollapse(e);
      HideNativeScrollBars();
      UpdateScrollBars();
    }

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
      base.OnNodeMouseClick(e);
      HideNativeScrollBars();
      UpdateScrollBars();
    }

    protected override void OnAfterSelect(TreeViewEventArgs e)
    {
      base.OnAfterSelect(e);
      HideNativeScrollBars();
      UpdateScrollBars();
    }

    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);
      HideNativeScrollBars();
      UpdateScrollBars();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
      base.OnVisibleChanged(e);
      if (Visible)
      {
        HideNativeScrollBars();
        UpdateScrollBars();
      }
    }

    protected override void WndProc(ref Message m)
    {
      base.WndProc(ref m);

      // Ensure native scrollbars stay hidden after style/size changes or scrolling
      switch (m.Msg)
      {
        case WM_VSCROLL:
        case WM_HSCROLL:
        case WM_MOUSEWHEEL:
          BeginInvoke(new Action(() =>
          {
            HideNativeScrollBars();
            SyncVScrollBarToTree();
            SyncHScrollBarToTree();
          }));
          break;

        case WM_SIZE:
        case WM_STYLECHANGED:
          BeginInvoke(new Action(() =>
          {
            HideNativeScrollBars();
            UpdateScrollBars();
          }));
          break;
      }
    }

    /// <summary>
    /// Updates both scrollbars based on TreeView's native scroll info.
    /// </summary>
    private void UpdateScrollBars()
    {
      UpdateVScrollBar();
      UpdateHScrollBar();
    }

    /// <summary>
    /// Updates the vertical scrollbar range based on TreeView's native scroll info.
    /// </summary>
    private void UpdateVScrollBar()
    {
      if (!IsHandleCreated || vScrollBar == null)
        return;

      try
      {
        int min, max;
        if (GetScrollRange(Handle, SB_VERT, out min, out max))
        {
          int visibleItems = GetVisibleItemCount();
          int totalItems = GetTotalVisibleNodeCount();

          if (totalItems > visibleItems && max > min)
          {
            vScrollBar.SetRange(min, max, visibleItems, 1);
            SyncVScrollBarToTree();
          }
          else
          {
            vScrollBar.SetRange(0, 0, 1, 1);
          }
        }
        else
        {
          vScrollBar.SetRange(0, 0, 1, 1);
        }
      }
      catch
      {
        vScrollBar.SetRange(0, 0, 1, 1);
      }
    }

    /// <summary>
    /// Updates the horizontal scrollbar range based on TreeView's native scroll info.
    /// </summary>
    private void UpdateHScrollBar()
    {
      if (!IsHandleCreated || hScrollBar == null)
        return;

      try
      {
        int min, max;
        if (GetScrollRange(Handle, SB_HORZ, out min, out max))
        {
          if (max > min)
          {
            int viewWidth = ClientSize.Width - vScrollBarHost.Width;
            hScrollBar.SetRange(min, max, viewWidth, 10);
            SyncHScrollBarToTree();
          }
          else
          {
            hScrollBar.SetRange(0, 0, 1, 1);
          }
        }
        else
        {
          hScrollBar.SetRange(0, 0, 1, 1);
        }
      }
      catch
      {
        hScrollBar.SetRange(0, 0, 1, 1);
      }
    }

    /// <summary>
    /// Syncs the vertical DarkScrollBar position to match TreeView's current scroll position.
    /// </summary>
    private void SyncVScrollBarToTree()
    {
      if (!IsHandleCreated || vScrollBar == null || suppressScrollSync)
        return;

      try
      {
        suppressScrollSync = true;
        int pos = GetScrollPos(Handle, SB_VERT);
        vScrollBar.Value = pos;
      }
      finally
      {
        suppressScrollSync = false;
      }
    }

    /// <summary>
    /// Syncs the horizontal DarkScrollBar position to match TreeView's current scroll position.
    /// </summary>
    private void SyncHScrollBarToTree()
    {
      if (!IsHandleCreated || hScrollBar == null || suppressScrollSync)
        return;

      try
      {
        suppressScrollSync = true;
        int pos = GetScrollPos(Handle, SB_HORZ);
        hScrollBar.Value = pos;
      }
      finally
      {
        suppressScrollSync = false;
      }
    }

    /// <summary>
    /// Handles vertical scrollbar value changes and scrolls the TreeView accordingly.
    /// </summary>
    private void OnVScrollBarValueChanged(int newValue)
    {
      if (!IsHandleCreated || suppressScrollSync)
        return;

      try
      {
        suppressScrollSync = true;
        SetScrollPos(Handle, SB_VERT, newValue, true);
        SendMessage(Handle, WM_VSCROLL, (IntPtr)(SB_THUMBPOSITION | (newValue << 16)), IntPtr.Zero);
        Invalidate();
      }
      finally
      {
        suppressScrollSync = false;
      }
    }

    /// <summary>
    /// Handles horizontal scrollbar value changes and scrolls the TreeView accordingly.
    /// </summary>
    private void OnHScrollBarValueChanged(int newValue)
    {
      if (!IsHandleCreated || suppressScrollSync)
        return;

      try
      {
        suppressScrollSync = true;
        SetScrollPos(Handle, SB_HORZ, newValue, true);
        SendMessage(Handle, WM_HSCROLL, (IntPtr)(SB_THUMBPOSITION | (newValue << 16)), IntPtr.Zero);
        Invalidate();
      }
      finally
      {
        suppressScrollSync = false;
      }
    }

    /// <summary>
    /// Gets approximate count of visible items in the TreeView viewport.
    /// </summary>
    private int GetVisibleItemCount()
    {
      if (Nodes.Count == 0)
        return 1;

      try
      {
        TreeNode firstVisible = TopNode;
        if (firstVisible == null)
          return 1;

        int itemHeight = firstVisible.Bounds.Height;
        if (itemHeight <= 0)
          itemHeight = 20;

        int visibleCount = Math.Max(1, ClientSize.Height / itemHeight);
        return visibleCount;
      }
      catch
      {
        return 1;
      }
    }

    /// <summary>
    /// Counts all visible (expanded) nodes in the tree.
    /// </summary>
    private int GetTotalVisibleNodeCount()
    {
      int count = 0;
      foreach (TreeNode node in Nodes)
      {
        count += CountVisibleNodes(node);
      }
      return Math.Max(1, count);
    }

    private int CountVisibleNodes(TreeNode node)
    {
      if (node == null)
        return 0;

      int count = 1;

      if (node.IsExpanded)
      {
        foreach (TreeNode child in node.Nodes)
        {
          count += CountVisibleNodes(child);
        }
      }

      return count;
    }

    /// <summary>
    /// Public method to refresh scrollbar state (call after adding/removing nodes).
    /// </summary>
    public void RefreshScrollBar()
    {
      HideNativeScrollBars();
      UpdateScrollBars();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (vScrollBar != null)
        {
          vScrollBar.UnhookAll();
          vScrollBar.Dispose();
          vScrollBar = null;
        }

        if (hScrollBar != null)
        {
          hScrollBar.UnhookAll();
          hScrollBar.Dispose();
          hScrollBar = null;
        }

        if (vScrollBarHost != null)
        {
          vScrollBarHost.Dispose();
          vScrollBarHost = null;
        }

        if (hScrollBarHost != null)
        {
          hScrollBarHost.Dispose();
          hScrollBarHost = null;
        }
      }

      base.Dispose(disposing);
    }

  } // class DarkTreeView

} // Namespace SwimEditor