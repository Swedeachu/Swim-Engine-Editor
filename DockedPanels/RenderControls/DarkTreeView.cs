using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace SwimEditor
{

  /// <summary>
  /// Custom dark-themed TreeView with integrated DarkScrollBar.
  /// Hides the native scrollbar and uses a custom vertical scrollbar.
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

    private const int SB_VERT = 1;
    private const int WM_VSCROLL = 0x0115;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_SIZE = 0x0005;
    private const int WM_STYLECHANGED = 0x007D;
    private const int SB_THUMBPOSITION = 4;

    // Window style flags to proactively suppress native scrollbars at creation time.
    private const int WS_HSCROLL = 0x00100000;
    private const int WS_VSCROLL = 0x00200000;

    private DarkScrollBar vScrollBar;
    private Panel scrollBarHost;
    private bool suppressScrollSync = false;

    public DarkTreeView()
    {
      // TreeView styling
      BorderStyle = BorderStyle.None;
      BackColor = SwimEditorTheme.PageBg;
      ForeColor = SwimEditorTheme.Fg;
      LineColor = SwimEditorTheme.Line;

      // Create host panel for scrollbar
      scrollBarHost = new Panel
      {
        Dock = DockStyle.Right,
        Width = 16,
        BackColor = SwimEditorTheme.PageBg
      };

      // Create dark scrollbar
      vScrollBar = new DarkScrollBar
      {
        Dock = DockStyle.Fill,
        AutoHide = true,
        Width = 16
      };

      // Wire up events
      vScrollBar.ScrollValueChanged += OnScrollBarValueChanged;

      // Add scrollbar to host, host to tree
      scrollBarHost.Controls.Add(vScrollBar);
      Controls.Add(scrollBarHost);

      // Hook the scrollbar to this control
      vScrollBar.SetScrollHooks(this, scrollBarHost, SyncScrollBarToTree);
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
      HideNativeScrollBar();
      UpdateScrollBar();
    }

    private void HideNativeScrollBar()
    {
      if (IsHandleCreated)
      {
        // Ensure the native scrollbar is hidden; call frequently after layout/size/style changes.
        ShowScrollBar(Handle, SB_VERT, false);
      }
    }

    protected override void OnAfterExpand(TreeViewEventArgs e)
    {
      base.OnAfterExpand(e);
      HideNativeScrollBar();
      UpdateScrollBar();
    }

    protected override void OnAfterCollapse(TreeViewEventArgs e)
    {
      base.OnAfterCollapse(e);
      HideNativeScrollBar();
      UpdateScrollBar();
    }

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
      base.OnNodeMouseClick(e);
      HideNativeScrollBar();
      UpdateScrollBar();
    }

    protected override void OnAfterSelect(TreeViewEventArgs e)
    {
      base.OnAfterSelect(e);
      HideNativeScrollBar();
      UpdateScrollBar();
    }

    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);

      // Critical fix: resizing could cause the OS to re-evaluate scroll need and re-show the native bar.
      // Hide it again here and then sync/update the custom bar.
      HideNativeScrollBar();
      UpdateScrollBar();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
      base.OnVisibleChanged(e);
      if (Visible)
      {
        HideNativeScrollBar();
        UpdateScrollBar();
      }
    }

    protected override void WndProc(ref Message m)
    {
      base.WndProc(ref m);

      // Ensure native scrollbar stays hidden after style/size changes or wheel scrolling.
      switch (m.Msg)
      {
        case WM_VSCROLL:
        case WM_MOUSEWHEEL:
          BeginInvoke(new Action(() =>
          {
            HideNativeScrollBar();
            SyncScrollBarToTree();
          }));
          break;

        case WM_SIZE:
        case WM_STYLECHANGED:
          BeginInvoke(new Action(() =>
          {
            HideNativeScrollBar();
            UpdateScrollBar();
          }));
          break;
      }
    }

    /// <summary>
    /// Updates the scrollbar range based on TreeView's native scroll info.
    /// </summary>
    private void UpdateScrollBar()
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

          // If there's actual scrollable content
          if (totalItems > visibleItems && max > min)
          {
            vScrollBar.SetRange(min, max, visibleItems, 1);
            SyncScrollBarToTree();
          }
          else
          {
            // No scrolling needed
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
    /// Syncs the DarkScrollBar position to match TreeView's current scroll position.
    /// </summary>
    private void SyncScrollBarToTree()
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
    /// Handles scrollbar value changes and scrolls the TreeView accordingly.
    /// </summary>
    private void OnScrollBarValueChanged(int newValue)
    {
      if (!IsHandleCreated || suppressScrollSync)
        return;

      try
      {
        suppressScrollSync = true;

        // Set the native scroll position
        SetScrollPos(Handle, SB_VERT, newValue, true);

        // Notify TreeView to redraw at new position
        SendMessage(Handle, WM_VSCROLL, (IntPtr)(SB_THUMBPOSITION | (newValue << 16)), IntPtr.Zero);

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
          itemHeight = 20; // default fallback

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

      int count = 1; // the node itself

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
      HideNativeScrollBar();
      UpdateScrollBar();
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

        if (scrollBarHost != null)
        {
          scrollBarHost.Dispose();
          scrollBarHost = null;
        }
      }

      base.Dispose(disposing);
    }

  } // class DarkTreeView

} // Namespace SwimEditor
