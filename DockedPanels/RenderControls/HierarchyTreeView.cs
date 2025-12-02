using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ReaLTaiizor.Controls;

namespace SwimEditor
{
  /// <summary>
  /// Thin wrapper over CrownTreeView that:
  ///   - Exposes vertical scroll value
  ///   - Provides BeginUpdate/EndUpdate to suspend painting during bulk updates
  /// </summary>
  public class HierarchyTreeView : CrownTreeView
  {
    private const int WM_SETREDRAW = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);

    private int updateNesting = 0;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int VerticalScrollValue
    {
      get
      {
        if (_vScrollBar == null)
        {
          return 0;
        }

        return _vScrollBar.Value;
      }

      set
      {
        if (_vScrollBar == null)
        {
          return;
        }

        try
        {
          // CrownScrollBar.Value already clamps to [Minimum, Maximum - ViewSize].
          _vScrollBar.Value = value;
        }
        catch
        {
          // Ignore out-of-range issues if scrollbar is mid-layout.
        }
      }
    }

    /// <summary>
    /// Suspends painting for this control (and children) using WM_SETREDRAW.
    /// Safe to call nested; only the outermost call actually toggles redraw.
    /// </summary>
    public void BeginUpdate()
    {
      if (!IsHandleCreated)
      {
        return;
      }

      updateNesting++;
      if (updateNesting == 1)
      {
        try
        {
          SendMessage(Handle, WM_SETREDRAW, false, 0);
        }
        catch
        {
          // Ignore any interop issues.
        }
      }
    }

    /// <summary>
    /// Resumes painting after BeginUpdate(). The last EndUpdate call will
    /// re-enable redraw and force a refresh.
    /// </summary>
    public void EndUpdate()
    {
      if (!IsHandleCreated)
      {
        return;
      }

      if (updateNesting == 0)
      {
        return;
      }

      updateNesting--;
      if (updateNesting == 0)
      {
        try
        {
          SendMessage(Handle, WM_SETREDRAW, true, 0);
          Invalidate();
          Update();
        }
        catch
        {
          // Ignore any interop issues.
        }
      }
    }

  } // class HierarchyTreeView

} // Namespace SwimEditor
