using System.ComponentModel;
using ReaLTaiizor.Controls;

namespace SwimEditor
{

  /// <summary>
  /// Thin wrapper over CrownTreeView that exposes vertical scroll value
  /// in a way that is designer-safe and does not require modifying the
  /// original ReaLTaiizor control code.
  /// </summary>
  public class HierarchyTreeView : CrownTreeView
  {

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
          // CrownScrollBar.Value already clamps to [Minimum, Maximum - ViewSize]
          _vScrollBar.Value = value;
        }
        catch
        {
          // Ignore out-of-range issues if scrollbar is mid-layout.
        }
      }
    }

  }

}
